using VideoProto;

namespace vidConfigs
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Add gRPC service to the dependency injection container
            services.AddGrpc();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            //app.MapWhen( context => context.Connection.LocalPort == 5001, iab => iab.UseRouting().UseEndpoints(endpoints => endpoints.MapGrpcService<VideoConsumer>()));

            //app.MapWhen(context => context.Connection.LocalPort == 5002, iab => iab.UseRouting().UseEndpoints(endpoints => endpoints.MapGrpcService<VideoConsumer>()));

            //app.MapWhen(context => context.Connection.LocalPort == 5003, iab => iab.UseRouting().UseEndpoints(endpoints => endpoints.MapGrpcService<VideoConsumer>()));

            //app.MapWhen(context => context.Connection.LocalPort == 5004, iab => iab.UseRouting().UseEndpoints(endpoints => endpoints.MapGrpcService<VideoConsumer>()));

            //app.MapWhen(context => context.Connection.LocalPort == 5005, iab => iab.UseRouting().UseEndpoints(endpoints => endpoints.MapGrpcService<VideoConsumer>()));

            app.UseEndpoints(endpoints =>
            {
                // Map the gRPC service
                endpoints.MapGrpcService<VideoConsumer>();

                // Optional: A fallback for non-gRPC requests
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("This server hosts gRPC services.");
                });
            });
        }
    }
}