using AttendanceBioApi.Models;
using AttendanceBioApi.Models.Biometric;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace AttendanceBioAPI.Service.Biometric;

public class ZktecoDevice : IBiometricDevice
{
    private const uint PacketMagic = 0x7D825050;

    private const ushort CmdConnect = 1000;
    private const ushort CmdExit = 1001;
    private const ushort CmdEnableDevice = 1002;
    private const ushort CmdDisableDevice = 1003;

    private const ushort CmdAuth = 1102;

    private const ushort CmdAttLogRequest = 13;

    private const ushort CmdPrepareData = 1500;
    private const ushort CmdData = 1501;
    private const ushort CmdFreeData = 1502;

    private const ushort CmdAckOk = 2000;
    private const ushort CmdAckError = 2001;
    private const ushort CmdAckData = 2002;
    private const ushort CmdAckRetry = 2003;
    private const ushort CmdAckRepeat = 2004;
    private const ushort CmdAckUnauth = 2005;

    private const int OuterHeaderSize = 8;
    private const int PayloadHeaderSize = 8;

    private const int AttendanceRecordSize = 40;
    private const int UserIdFieldLength = 24;

    private const int ResponseTimeoutSeconds = 10;

    private readonly ILogger<ZktecoDevice> _logger;
    private readonly BiometricDevice _device;

    private TcpClient? _client;
    private NetworkStream? _stream;

    private ushort _sessionId;
    private ushort _replyId;

    private bool _isAuthenticated;

    public string DeviceId => _device.DeviceId;

    public string SiteId => _device.BranchCode;

    public ZktecoDevice(
        BiometricDevice device,
        ILogger<ZktecoDevice> logger)
    {
        _device = device;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DisconnectAsync();

            _client = new TcpClient();

            _logger.LogInformation(
                "Connecting to ZKTeco {DeviceId} at {IpAddress}:{Port}. " +
                "Model={Model}, CommKey={CommKey}",
                _device.DeviceId,
                _device.IpAddress,
                _device.Port,
                _device.Model,
                _device.CommKey);

            await _client.ConnectAsync(
                _device.IpAddress,
                _device.Port,
                cancellationToken);

            _stream = _client.GetStream();

            _sessionId = 0;
            _replyId = 0;
            _isAuthenticated = false;

            _logger.LogInformation(
                "TCP connection established to ZKTeco {IpAddress}:{Port}",
                _device.IpAddress,
                _device.Port);

            var response = await SendCommandAsync(
                CmdConnect,
                Array.Empty<byte>(),
                cancellationToken,
                incrementReplyAfterResponse: false);

            _logger.LogDebug(
                "ZKTeco CONNECT response: Command={Command}, " +
                "Session={SessionId}, Reply={ReplyId}, DataLength={DataLength}",
                response.Command,
                response.SessionId,
                response.ReplyId,
                response.Data.Length);

            if (response.Command == CmdAckUnauth)
            {
                _sessionId = response.SessionId;

                _logger.LogInformation(
                    "ZKTeco device requested authentication. SessionId={SessionId}",
                    _sessionId);

                var authData = MakeCommKey(
                    _device.CommKey,
                    _sessionId);

                var authResponse = await SendCommandAsync(
                    CmdAuth,
                    authData,
                    cancellationToken,
                    incrementReplyAfterResponse: false);

                _logger.LogDebug(
                    "ZKTeco AUTH response: Command={Command}, " +
                    "Session={SessionId}, Reply={ReplyId}, DataLength={DataLength}",
                    authResponse.Command,
                    authResponse.SessionId,
                    authResponse.ReplyId,
                    authResponse.Data.Length);

                if (authResponse.Command != CmdAckOk)
                {
                    _logger.LogError(
                        "ZKTeco authentication failed. " +
                        "Command={Command}, SessionId={SessionId}",
                        authResponse.Command,
                        _sessionId);

                    await CloseSocketAsync();

                    return false;
                }

                _isAuthenticated = true;

                _replyId = NextReplyId(
                    authResponse.ReplyId);

                _logger.LogInformation(
                    "ZKTeco authentication successful. SessionId={SessionId}",
                    _sessionId);

                return true;
            }

            if (response.Command == CmdAckOk)
            {
                _sessionId = response.SessionId;

                _replyId = NextReplyId(
                    response.ReplyId);

                _isAuthenticated = true;

                _logger.LogInformation(
                    "ZKTeco connection accepted without authentication. " +
                    "SessionId={SessionId}",
                    _sessionId);

                return true;
            }

            _logger.LogError(
                "ZKTeco device {DeviceId} returned unexpected " +
                "CONNECT response {Command}",
                _device.DeviceId,
                response.Command);

            await CloseSocketAsync();

            return false;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "ZKTeco device {DeviceId} did not respond within " +
                "{Timeout} seconds during connection/authentication.",
                _device.DeviceId,
                ResponseTimeoutSeconds);

            await CloseSocketAsync();

            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Connection to ZKTeco device {DeviceId} was cancelled.",
                _device.DeviceId);

            await CloseSocketAsync();

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to ZKTeco device {DeviceId} " +
                "at {IpAddress}:{Port}",
                _device.DeviceId,
                _device.IpAddress,
                _device.Port);

            await CloseSocketAsync();

            return false;
        }
    }

    public async Task<IReadOnlyCollection<AttendancePunch>> GetAttendancePunchesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            _logger.LogInformation(
                "Reading attendance from ZKTeco device {DeviceId}",
                _device.DeviceId);

            var response = await SendCommandAsync(
                CmdAttLogRequest,
                Array.Empty<byte>(),
                cancellationToken);

            _logger.LogDebug(
                "Attendance response: Command={Command}, Session={Session}, " +
                "Reply={Reply}, DataLength={Length}",
                response.Command,
                response.SessionId,
                response.ReplyId,
                response.Data.Length);

            if (response.Command == CmdPrepareData)
            {
                if (response.Data.Length < 4)
                {
                    throw new InvalidOperationException(
                        "ZKTeco PREPARE_DATA response did not contain " +
                        "the expected data size.");
                }

                var expectedDataLength =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        response.Data.AsSpan(0, 4));

                _logger.LogInformation(
                    "ZKTeco prepared attendance data. " +
                    "Expected data length={Length} bytes",
                    expectedDataLength);

                var dataResponse = await ReadPacketAsync(
                    cancellationToken);

                _logger.LogDebug(
                    "ZKTeco DATA packet received. " +
                    "Command={Command}, Session={Session}, " +
                    "Reply={Reply}, DataLength={Length}",
                    dataResponse.Command,
                    dataResponse.SessionId,
                    dataResponse.ReplyId,
                    dataResponse.Data.Length);

                if (dataResponse.Command != CmdData)
                {
                    throw new InvalidOperationException(
                        "ZKTeco returned unexpected data command: " +
                        $"{dataResponse.Command}");
                }

                if (dataResponse.Data.Length != expectedDataLength)
                {
                    _logger.LogWarning(
                        "ZKTeco data length mismatch. " +
                        "Expected={Expected}, Actual={Actual}",
                        expectedDataLength,
                        dataResponse.Data.Length);
                }

                return ParseAttendanceData(
                    dataResponse.Data);
            }

            if (response.Command == CmdAckData ||
                response.Command == CmdAckOk)
            {
                if (response.Data.Length == 0)
                {
                    _logger.LogInformation(
                        "ZKTeco returned no attendance data.");

                    return Array.Empty<AttendancePunch>();
                }

                _logger.LogDebug(
                    "ZKTeco returned attendance data directly. " +
                    "Length={Length}",
                    response.Data.Length);

                return ParseAttendanceData(
                    response.Data);
            }

            throw new InvalidOperationException(
                "ZKTeco returned unexpected attendance response: " +
                $"{response.Command}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Reading attendance from ZKTeco device {DeviceId} " +
                "was cancelled.",
                _device.DeviceId);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read attendance punches from " +
                "ZKTeco device {DeviceId}",
                _device.DeviceId);

            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_isAuthenticated &&
                _stream != null &&
                _client != null &&
                _client.Connected)
            {
                try
                {
                    _logger.LogDebug(
                        "Sending ZKTeco EXIT command for device {DeviceId}",
                        _device.DeviceId);

                    await SendCommandAsync(
                        CmdExit,
                        Array.Empty<byte>(),
                        CancellationToken.None,
                        incrementReplyAfterResponse: false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "ZKTeco EXIT command failed during cleanup");
                }
            }
        }
        finally
        {
            await CloseSocketAsync();
        }
    }

    private Task CloseSocketAsync()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
        }

        _stream = null;
        _client = null;

        _sessionId = 0;
        _replyId = 0;
        _isAuthenticated = false;

        return Task.CompletedTask;
    }

    private async Task<ZkPacket> SendCommandAsync(
        ushort command,
        byte[] data,
        CancellationToken cancellationToken,
        bool incrementReplyAfterResponse = true)
    {
        EnsureSocketConnected();

        data ??= Array.Empty<byte>();

        var currentReplyId = _replyId;

        var packet = BuildPacket(
            command,
            data,
            _sessionId,
            currentReplyId);

        _logger.LogDebug(
            "ZKTeco SEND: Command={Command}, Session={SessionId}, " +
            "Reply={ReplyId}, DataLength={DataLength}, " +
            "PacketLength={PacketLength}",
            command,
            _sessionId,
            currentReplyId,
            data.Length,
            packet.Length);

        await _stream!.WriteAsync(
            packet,
            cancellationToken);

        await _stream.FlushAsync(
            cancellationToken);

        var response = await ReadPacketAsync(
            cancellationToken);

        _logger.LogDebug(
            "ZKTeco RECEIVE: Command={Command}, Session={SessionId}, " +
            "Reply={ReplyId}, Checksum={Checksum}, DataLength={DataLength}",
            response.Command,
            response.SessionId,
            response.ReplyId,
            response.Checksum,
            response.Data.Length);

        if (incrementReplyAfterResponse)
        {
            _replyId = NextReplyId(
                response.ReplyId);
        }

        return response;
    }

    private static byte[] BuildPacket(
        ushort command,
        byte[] data,
        ushort sessionId,
        ushort replyId)
    {
        data ??= Array.Empty<byte>();

        var payloadLength =
            PayloadHeaderSize + data.Length;

        var packet =
            new byte[
                OuterHeaderSize + payloadLength];

        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(0, 4),
            PacketMagic);

        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(4, 4),
            (uint)payloadLength);

        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(8, 2),
            command);

        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(10, 2),
            0);

        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(12, 2),
            sessionId);

        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(14, 2),
            replyId);

        if (data.Length > 0)
        {
            Buffer.BlockCopy(
                data,
                0,
                packet,
                16,
                data.Length);
        }

        var checksumData =
            packet.AsSpan(8).ToArray();

        checksumData[2] = 0;
        checksumData[3] = 0;

        var checksum =
            CalculateChecksum(
                checksumData);

        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(10, 2),
            checksum);

        return packet;
    }

    private async Task<ZkPacket> ReadPacketAsync(
        CancellationToken cancellationToken)
    {
        EnsureSocketConnected();

        var header =
            new byte[OuterHeaderSize];

        await ReadExactlyAsync(
            _stream!,
            header,
            cancellationToken);

        var magic =
            BinaryPrimitives.ReadUInt32LittleEndian(
                header.AsSpan(0, 4));

        if (magic != PacketMagic)
        {
            throw new IOException(
                $"Invalid ZKTeco packet magic: {magic:X8}. " +
                $"Expected {PacketMagic:X8}.");
        }

        var payloadLength =
            BinaryPrimitives.ReadUInt32LittleEndian(
                header.AsSpan(4, 4));

        if (payloadLength < PayloadHeaderSize)
        {
            throw new IOException(
                $"Invalid ZKTeco payload length: {payloadLength}.");
        }

        const uint MaxPayloadSize =
            16 * 1024 * 1024;

        if (payloadLength > MaxPayloadSize)
        {
            throw new IOException(
                $"ZKTeco payload is too large: " +
                $"{payloadLength} bytes.");
        }

        var payload =
            new byte[(int)payloadLength];

        await ReadExactlyAsync(
            _stream!,
            payload,
            cancellationToken);

        var command =
            BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(0, 2));

        var checksum =
            BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(2, 2));

        var sessionId =
            BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(4, 2));

        var replyId =
            BinaryPrimitives.ReadUInt16LittleEndian(
                payload.AsSpan(6, 2));

        var dataLength =
            payload.Length -
            PayloadHeaderSize;

        var data =
            new byte[dataLength];

        if (dataLength > 0)
        {
            Buffer.BlockCopy(
                payload,
                PayloadHeaderSize,
                data,
                0,
                dataLength);
        }

        var checksumData =
            payload.ToArray();

        checksumData[2] = 0;
        checksumData[3] = 0;

        var calculatedChecksum =
            CalculateChecksum(
                checksumData);

        if (calculatedChecksum != checksum)
        {
            _logger.LogWarning(
                "ZKTeco checksum mismatch. " +
                "Received={ReceivedChecksum}, " +
                "Calculated={CalculatedChecksum}, " +
                "Command={Command}",
                checksum,
                calculatedChecksum,
                command);
        }

        return new ZkPacket
        {
            Command = command,
            Checksum = checksum,
            SessionId = sessionId,
            ReplyId = replyId,
            Data = data
        };
    }

    private async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(
                ResponseTimeoutSeconds));

        while (offset < buffer.Length)
        {
            var bytesRead =
                await stream.ReadAsync(
                    buffer.AsMemory(offset),
                    timeoutCts.Token);

            if (bytesRead == 0)
            {
                throw new IOException(
                    "ZKTeco device closed the connection.");
            }

            offset += bytesRead;
        }
    }

    private static ushort CalculateChecksum(
        byte[] packet)
    {
        uint checksum = 0;

        var length = packet.Length;

        var index = 0;

        while (length > 1)
        {
            var value =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    packet.AsSpan(index, 2));

            checksum += value;

            while (checksum > 0xFFFF)
            {
                checksum -= 0xFFFF;
            }

            index += 2;
            length -= 2;
        }

        if (length > 0)
        {
            checksum += packet[index];

            while (checksum > 0xFFFF)
            {
                checksum -= 0xFFFF;
            }
        }

        checksum =
            (~checksum) & 0xFFFF;

        return (ushort)checksum;
    }

    private static byte[] MakeCommKey(
        int key,
        ushort sessionId,
        byte ticks = 50)
    {
        uint k = 0;

        uint unsignedKey =
            unchecked((uint)key);

        for (var i = 0; i < 32; i++)
        {
            k <<= 1;

            if ((unsignedKey & (1u << i)) != 0)
            {
                k |= 1;
            }
        }

        k = unchecked(
            k + sessionId);

        var bytes =
            BitConverter.GetBytes(k);

        bytes[0] ^= (byte)'Z';
        bytes[1] ^= (byte)'K';
        bytes[2] ^= (byte)'S';
        bytes[3] ^= (byte)'O';

        var first =
            BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(0, 2));

        var second =
            BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(2, 2));

        var swapped =
            new byte[4];

        BinaryPrimitives.WriteUInt16LittleEndian(
            swapped.AsSpan(0, 2),
            second);

        BinaryPrimitives.WriteUInt16LittleEndian(
            swapped.AsSpan(2, 2),
            first);

        var tick =
            (byte)(ticks & 0xFF);

        swapped[0] ^= tick;
        swapped[1] ^= tick;
        swapped[2] = tick;
        swapped[3] ^= tick;

        return swapped;
    }

    private static ushort NextReplyId(
        ushort replyId)
    {
        if (replyId >= ushort.MaxValue - 1)
        {
            return 0;
        }

        return (ushort)(replyId + 1);
    }

    private List<AttendancePunch> ParseAttendanceData(byte[] data)
    {
        var punches = new List<AttendancePunch>();

        _logger.LogInformation(
            "Parsing {Bytes} bytes of ZKTeco attendance data from device {DeviceId}",
            data.Length,
            _device.DeviceId);

        if (data.Length < 4)
        {
            _logger.LogWarning(
                "ZKTeco attendance data is too short: {Length} bytes.",
                data.Length);

            return punches;
        }

        uint declaredDataSize =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(0, 4));

        int availableBytes = data.Length - 4;

        int recordCount =
            availableBytes / AttendanceRecordSize;

        int remainder =
            availableBytes % AttendanceRecordSize;

        _logger.LogInformation(
            "ZKTeco declared size={Size} bytes, records={Count}, " +
            "remaining bytes={Remainder}.",
            declaredDataSize,
            recordCount,
            remainder);

        if (remainder != 0)
        {
            _logger.LogWarning(
                "ZKTeco attendance data contains {Remainder} extra byte(s) " +
                "after the {RecordSize}-byte records.",
                remainder,
                AttendanceRecordSize);
        }

        var skipped = 0;

        for (int i = 0; i < recordCount; i++)
        {
            int offset =
                4 + (i * AttendanceRecordSize);

            var record =
                data.AsSpan(offset, AttendanceRecordSize);

            try
            {
                ushort uid =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        record.Slice(0, 2));

                string biometricId =
                    Encoding.ASCII
                        .GetString(record.Slice(2, UserIdFieldLength))
                        .TrimEnd('\0', ' ')
                        .Trim();

                byte verifyMode =
                    record[26];

                uint packedTime =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(27, 4));

                byte attendanceState =
                    record[31];

                uint workCode =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        record.Slice(32, 4));

                DateTime punchTime =
                    DecodeZkTime(packedTime);

                _logger.LogDebug(
                    "RECORD {Index}: UID={Uid}, BiometricId=[{BiometricId}], " +
                    "VerifyMode={VerifyMode}, PackedTime=0x{PackedTime:X8}, " +
                    "Time={PunchTime}, State={State}, WorkCode={WorkCode}",
                    i + 1,
                    uid,
                    biometricId,
                    verifyMode,
                    packedTime,
                    punchTime == DateTime.MinValue
                        ? "INVALID"
                        : punchTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    attendanceState,
                    workCode);

                if (string.IsNullOrWhiteSpace(biometricId))
                {
                    skipped++;

                    _logger.LogWarning(
                        "Skipping record {Index}: empty biometric ID. UID={Uid}",
                        i + 1,
                        uid);

                    continue;
                }

                if (punchTime == DateTime.MinValue)
                {
                    skipped++;

                    _logger.LogWarning(
                        "Skipping record {Index}: invalid timestamp. " +
                        "PackedTime=0x{PackedTime:X8}, UID={Uid}",
                        i + 1,
                        packedTime,
                        uid);

                    continue;
                }

                punches.Add(new AttendancePunch
                {
                    BiometricId = biometricId,
                    PunchTime = punchTime,
                    PunchState = attendanceState.ToString(),
                    DeviceId = _device.DeviceId
                });
            }
            catch (Exception ex)
            {
                skipped++;

                _logger.LogWarning(
                    ex,
                    "Failed to parse ZKTeco attendance record {Index} " +
                    "at offset {Offset}.",
                    i + 1,
                    offset);
            }
        }

        _logger.LogInformation(
            "Parsed {Count} attendance records from device {DeviceId}. " +
            "Skipped={Skipped}.",
            punches.Count,
            _device.DeviceId,
            skipped);

        return punches;
    }

    private DateTime DecodeZkTime(uint value)
    {
        uint t = value;

        int second = (int)(t % 60);
        t /= 60;

        int minute = (int)(t % 60);
        t /= 60;

        int hour = (int)(t % 24);
        t /= 24;

        int day = (int)(t % 31) + 1;
        t /= 31;

        int month = (int)(t % 12) + 1;
        t /= 12;

        int year = (int)t + 2000;

        try
        {
            return new DateTime(year, month, day, hour, minute, second);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed constructing ZK timestamp from 0x{Value:X8}",
                value);

            return DateTime.MinValue;
        }
    }

    private void EnsureSocketConnected()
    {
        if (_client == null ||
            !_client.Connected ||
            _stream == null)
        {
            throw new InvalidOperationException(
                $"ZKTeco device '{_device.DeviceId}' " +
                $"does not have an active TCP connection.");
        }
    }

    private void EnsureConnected()
    {
        EnsureSocketConnected();

        if (!_isAuthenticated)
        {
            throw new InvalidOperationException(
                $"ZKTeco device '{_device.DeviceId}' " +
                $"is connected but not authenticated.");
        }
    }
}
