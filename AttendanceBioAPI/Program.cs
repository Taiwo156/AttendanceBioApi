using AttendanceBioAPI.Services;
using AttendanceBioApi.Data;
using AttendanceBioAPI.Service;
using AttendanceBioAPI.Service.Auth;
using AttendanceBioAPI.Service.Biometric;
using AttendanceBioAPI.Service.Helpers;
using AttendanceBioAPI.Service.Interfaces;
using AttendanceBioAPI.Service.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using AttendanceBioApi.Data.Database;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Data.Repo;
using AttendanceBioAPI.Data.Auth;
using AttendanceBioAPI.Hubs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt"));

var jwtOptions = builder.Configuration
    .GetSection("Jwt")
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        "JWT configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "JWT signing key is missing.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Key)),

            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

var dashboardOrigin = builder.Configuration["Dashboard:Origin"] ?? "*";

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<IAttendanceEventsPublisher, AttendanceEventsPublisher>();
builder.Services.AddSingleton<IAttendanceSignalRPublisher, SignalRAttendanceEventsPublisher>();
builder.Services.AddScoped<IAttendanceQueryService, AttendanceQueryService>();
builder.Services.AddScoped<IDeviceSyncService, DeviceSyncService>();

builder.Services.AddSingleton<IZkbioService, ZkbioService>();
builder.Services.AddSingleton<IEmployeeResolutionService, EmployeeResolutionService>();

builder.Services.AddSingleton<IBiometricDeviceRepository,BiometricDeviceRepository>();
builder.Services.AddSingleton<IBiometricDeviceFactory,BiometricDeviceFactory>();
builder.Services.AddSingleton<ICompanyRepository, CompanyRepository>();

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddScoped<SecurityHelper>();

builder.Services.AddScoped<IUserLocationScopeRepository, UserLocationScopeRepository>();

builder.Services.AddScoped<IRoleLocationScopeRepository, RoleLocationScopeRepository>();

builder.Services.AddSingleton<CompanyResolutionService>();
builder.Services.AddSingleton<ICompanyResolutionService>(sp => sp.GetRequiredService<CompanyResolutionService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CompanyResolutionService>());

builder.Services.AddSingleton<AttendancePollerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AttendancePollerService>());
builder.Services.AddHostedService<OvertimePollerService>();
builder.Services.AddHostedService<RedisSubscriberService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DashboardPolicy", policy =>
    {
        if (dashboardOrigin == "*")
        {
            policy.SetIsOriginAllowed(origin => true) 
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials(); 
        }
        else
        {
            policy.WithOrigins(dashboardOrigin)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AttendanceBioApi V1");
});
app.UseRouting();

app.UseCors("DashboardPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<AttendanceHub>("/attendance");

app.Run();


