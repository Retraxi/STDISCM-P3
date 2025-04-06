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
        [STAThread]
        public static void Main(string[] args)
        {
            var grpcThread = new Thread(() =>
            {
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddGrpc();

                var app = builder.Build();
                app.MapGrpcService<VideoConsumer>();
                app.MapGet("/", () => "gRPC server running");

                app.Run();
            });
            //var builder = WebApplication.CreateBuilder(args);

            //// Add services to the container.
            //builder.Services.AddGrpc();

            //var app = builder.Build();

            //// Configure the HTTP request pipeline.
            //app.MapGrpcService<VideoConsumer>();
            //app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

            //app.Run();
            grpcThread.IsBackground = true;
            grpcThread.Start();

            // Start WPF application
            var app = new App();
            var mainWindow = new MainWindow();
            app.Run(mainWindow);
        }
    }
}