using System;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Wow.Mailers.Unicorn.Implementation;
// Version to Modify!!!
namespace Wow.Mailers.Unicorn
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"Config/appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .CreateLogger();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHangfire(config => config.UsePostgreSqlStorage(Configuration["HangFire:ConnectionString"]))
                .AddLogging()
                .AddSingleton(Configuration);
        }

        /// <summary>
        /// Configures the specified application.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <param name="env">The env.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="appLifetime">The application lifetime.</param>
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime appLifetime)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VIRTUAL_PATH")))
                app.Map(Environment.GetEnvironmentVariable("VIRTUAL_PATH"), (myAppPath) => Configure2(myAppPath, env, loggerFactory, appLifetime));
            else
                Configure2(app, env, loggerFactory, appLifetime);
        }


        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure2(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime appLifetime)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();
            loggerFactory.AddSerilog();

            appLifetime.ApplicationStopped.Register(Log.CloseAndFlush);

            var options = new BackgroundJobServerOptions
            {
                Queues = new[] { "mailer", "default" }
            };

            app.UseHangfireServer(options);
            app.UseHangfireDashboard();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var messageProcesor = new MessageProcessor(Configuration);
            RecurringJob.AddOrUpdate("unicorn", () => messageProcesor.ProcessMessages(), Cron.MinuteInterval(1), TimeZoneInfo.Utc, "mailer");
            //RecurringJob.AddOrUpdate("unicorn", () => Console.WriteLine("Test Writing to Console"), Cron.MinuteInterval(1), TimeZoneInfo.Utc, "mailer");
        }
    }
}
