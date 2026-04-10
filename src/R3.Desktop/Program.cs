using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using R3.Desktop.Forms;
using R3.Desktop.Platform;
using R3.Infrastructure;

namespace R3.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WindowsAppIdentity.Initialize();
        ApplicationConfiguration.Initialize();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddR3Infrastructure(context.Configuration);
                services.AddTransient<LoginForm>();
                services.AddTransient<MainForm>();
            })
            .Build();

        using IServiceScope scope = host.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        using LoginForm login = services.GetRequiredService<LoginForm>();
        if (login.ShowDialog() != DialogResult.OK)
            return;

        System.Windows.Forms.Application.Run(services.GetRequiredService<MainForm>());
    }
}
