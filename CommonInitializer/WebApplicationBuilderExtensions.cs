﻿using FluentValidation.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Noodles.ASPNETCore;
using Noodles.Common;
using Noodles.Common.JsonConverters;
using Noodles.EventBus;
using Noodles.Infrastructure.EFCore;
using Noodles.Jwt;
using Serilog;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CommonInitializer
{
    public static class WebApplicationBuilderExtensions
    {
        
        public static void ConfigureDbConfiguration(this WebApplicationBuilder builder)
        {
            builder.Host.ConfigureAppConfiguration((hostCtx, configBuilder) =>
            {
                IConfiguration configuration = builder.Configuration;
                string connStr = configuration.GetSection("ConnectionString").Value;
                configBuilder.AddDbConfiguration(() => new SqlConnection(connStr), reloadOnChange: true, reloadInterval: TimeSpan.FromSeconds(5));
            });
        }

        public static void ConfigureExtraServices(this WebApplicationBuilder builder, InitializerOptions initOptions)
        {
            IServiceCollection services = builder.Services;
            IConfiguration configuration = builder.Configuration;
            var assemblies = ReflectionHelper.GetAllReferencedAssemblies();
            services.RunModuleInitializers(assemblies);
            services.AddAllDbContexts(ctx =>
            {
                //连接字符串如果放到appsettings.json中，会有泄密的风险
                //如果放到UserSecrets中，每个项目都要配置，很麻烦
                //因此这里推荐放到环境变量中。
                IConfiguration configuration = builder.Configuration;
                string connStr = configuration.GetSection("ConnectionString").Value;
                ctx.UseSqlServer(connStr);
            }, assemblies);

            //开始:Authentication,Authorization
            //只要需要校验Authentication报文头的地方（非IdentityService.WebAPI项目）也需要启用这些
            //IdentityService项目还需要启用AddIdentityCore
            builder.Services.AddAuthorization();
            builder.Services.AddAuthentication();
            JwtOptions jwtOpt = configuration.GetSection("JWT").Get<JwtOptions>();
            builder.Services.AddJWTAuthentication(jwtOpt);
            //启用Swagger中的【Authorize】按钮。这样就不用每个项目的AddSwaggerGen中单独配置了
            builder.Services.Configure<SwaggerGenOptions>(c =>
            {
                c.AddAuthenticationHeader();
            });
            //结束:Authentication,Authorization

            services.AddMediatR(assemblies);
            //现在不用手动AddMVC了，因此把文档中的services.AddMvc(options =>{})改写成Configure<MvcOptions>(options=> {})这个问题很多都类似
            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add<UnitOfWorkFilter>();
            });
            services.Configure<JsonOptions>(options =>
            {
                //设置时间格式。而非“2008-08-08T08:08:08”这样的格式
                options.JsonSerializerOptions.Converters.Add(new DateTimeJsonConverter("yyyy-MM-dd HH:mm:ss"));
            });

            services.AddCors(options =>
                {
                    //更好的在Program.cs中用绑定方式读取配置的方法：https://github.com/dotnet/aspnetcore/issues/21491
                    //不过比较麻烦。
                    var corsOpt = configuration.GetSection("Cors").Get<CorsSettings>();
                    string[] urls = corsOpt.Origins;
                    options.AddDefaultPolicy(builder => builder.WithOrigins(urls)
                            .AllowAnyMethod().AllowAnyHeader().AllowCredentials());
                }
            );
            services.AddLogging(builder =>
            {
                Log.Logger = new LoggerConfiguration()
                   // .MinimumLevel.Information().Enrich.FromLogContext()
                   .WriteTo.Console()
                   .WriteTo.File(initOptions.LogFilePath)
                   .CreateLogger();
                builder.AddSerilog();
            });
            services.AddFluentValidation(fv =>
            {                
                fv.RegisterValidatorsFromAssemblies(assemblies);
            });
            services.Configure<JwtOptions>(configuration.GetSection("JWT"));
            services.Configure<IntegrationEventRabbitMQOptions>(configuration.GetSection("RabbitMQ"));
            services.AddEventBus(initOptions.EventBusQueueName, assemblies);

            //Redis的配置
            string redisConnStr = configuration.GetValue<string>("Redis:ConnStr");
            IConnectionMultiplexer redisConnMultiplexer = ConnectionMultiplexer.Connect(redisConnStr);
            services.AddSingleton(typeof(IConnectionMultiplexer), redisConnMultiplexer);
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
            });
        }
    }
}
