namespace AttendanceBioApi.Models.Biometric;

public class ZkPacket
{
    public ushort Command { get; set; }

    public ushort Checksum { get; set; }

    public ushort SessionId { get; set; }

    public ushort ReplyId { get; set; }

    public byte[] Data { get; set; } = Array.Empty<byte>();
}