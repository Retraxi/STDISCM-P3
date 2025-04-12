//using Microsoft.AspNetCore.Hosting;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using System;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Windows;
//using VideoPlayerApp;
//using VideoProto;
//using YourNamespace;

//namespace GRPC_Test2
//{
//    public class Program
//    {
//        [STAThread]
//        public static void Main(string[] args)
//        {
//            var grpcThread = new Thread(() =>
//            {
//                var builder = WebApplication.CreateBuilder(args);
//                builder.Services.AddGrpc();

//                var app = builder.Build();
//                app.MapGrpcService<VideoConsumer>();
//                app.MapGet("/", () => "gRPC server running");

//                app.Run();
//            });

//            grpcThread.IsBackground = true;
//            grpcThread.Start();

//            // Start WPF application
//            var app = new App();
//            var mainWindow = new MainWindow();
//            app.Run(mainWindow);
//        }
//    }
//}
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VideoPlayerApp;
using VideoProto;
using YourNamespace;

namespace GRPC_Test2
{
    public class Program
    {
        private static CancellationTokenSource _grpcCts = new();

        [STAThread]
        public static void Main(string[] args)
        {
            Task.Run(() => StartGrpcServer(_grpcCts.Token));

            var app = new App();
            var mainWindow = new MainWindow();

            // Hook into the Exit event to shut down gRPC
            app.Exit += OnAppExit;

            app.Run(mainWindow);
        }

        private static async Task StartGrpcServer(CancellationToken cancellationToken)
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddGrpc();
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGrpcService<VideoConsumer>();
                            endpoints.MapGet("/", async context =>
                            {
                                await context.Response.WriteAsync("gRPC server running");
                            });
                        });
                    });
                });

            var host = builder.Build();

            await host.RunAsync(cancellationToken); // supports graceful shutdown
        }

        private static void OnAppExit(object sender, ExitEventArgs e)
        {
            // Cancel the token to shut down gRPC server
            _grpcCts.Cancel();
            _grpcCts.Dispose();
        }
    }
}
