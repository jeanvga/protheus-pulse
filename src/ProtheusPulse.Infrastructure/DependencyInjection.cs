using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtheusPulse.Application.Abstractions;
using ProtheusPulse.Infrastructure.Dashboard;
using ProtheusPulse.Infrastructure.Demo;
using ProtheusPulse.Infrastructure.Monitoring;
using ProtheusPulse.Infrastructure.Persistence;
using ProtheusPulse.Infrastructure.Security;
using ProtheusPulse.Infrastructure.Time;

namespace ProtheusPulse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPulseInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PulseDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordService, Pbkdf2PasswordService>();
        services.AddScoped<IDashboardQuery, EfDashboardQuery>();
        services.AddScoped<IDemoDataSeeder, DemoDataSeeder>();
        services.AddSingleton<IProbeCollector, WindowsServiceProbeCollector>();
        services.AddSingleton<IProbeCollector, ProcessProbeCollector>();
        services.AddSingleton<IProbeCollector, TcpProbeCollector>();
        services.AddSingleton<IProbeCollector, HttpProbeCollector>();
        services.AddSingleton<IProbeCollector, TlsCertificateProbeCollector>();
        services.AddSingleton<IProbeCollector, FileProbeCollector>();
        services.AddSingleton<IProbeCollector, DiskProbeCollector>();
        services.AddSingleton<IIncrementalLogCollector, IncrementalLogCollector>();
        return services;
    }
}
