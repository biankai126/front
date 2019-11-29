using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Slzhly.Core.Utils;
using Slzhly.Core.Web.Authentication.WeChat;
using Slzhly.Core.Web.Authorization;
using Slzhly.Core.Web.Extension;
using Slzhly.Core.Web.Filters;
using System;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;

namespace Slzhly.BaseApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        /// <summary>
        /// Gets or sets �������
        /// </summary>
        public static string PolicyName { get; set; } = "allow_all";

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCustomMvc(Configuration)
                .AddHealthChecks(Configuration)
                .AddHttpClient()
                .AddMemoryCache()
                .AddDistributedMemoryCache()
                .AddCustomSwagger(Configuration)
                .AddCustomConfiguration(Configuration);
            //services.AddAuthentication(BearerAuthorizeAttribute.DefaultAuthenticationScheme)
            //    .AddCookie(BearerAuthorizeAttribute.DefaultAuthenticationScheme, o =>
            //    {
            //        o.Cookie.Name = BearerAuthorizeAttribute.DefaultAuthenticationScheme;
            //        o.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            //    }).AddWeChat(wechatOptions =>
            //    {
            //        wechatOptions.CallbackPath = new PathString($"/{Program.AppName}/signin-wechat");
            //        wechatOptions.CallbackUrl = $"{Configuration["WeChat:CallbackPath"]}/{Program.AppName}/signin-wechat";
            //        wechatOptions.AppId = Configuration["WeChat:AppId"];
            //        wechatOptions.AppSecret = Configuration["WeChat:AppSecret"];
            //        wechatOptions.UseCachedStateDataFormat = true;
            //    });
        }

        /// <summary>
        /// ConfigureContainer is where you can register things directly with Autofac. This runs after ConfigureServices so the things
        /// here will override registrations made in ConfigureServices.
        /// Don't build the container; that gets done for you. If you
        /// need a reference to the container, you need to use the
        /// "Without ConfigureContainer" mechanism shown later.
        /// </summary>
        public void ConfigureContainer(ContainerBuilder builder)
        {
            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseStaticFiles();
            app.UseErrorHandling();
            app.UseRouting();
            app.UseCors(Startup.PolicyName);
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSwagger(c => { c.RouteTemplate = "doc/{documentName}/swagger.json"; })
               .UseSwaggerUI(c =>
               {
                   c.SwaggerEndpoint($"/doc/{Program.AppName}/swagger.json", $"{Program.AppName} {Configuration["Version"]}");
               });
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/hc", new HealthCheckOptions()
                {
                    Predicate = _ => true
                });
                endpoints.MapControllers();
            });
        }
    }
    /// <summary>
    /// �Զ�����չ
    /// </summary>
    public static class CustomExtensionsMethods
    {
        public static IServiceCollection AddCustomMvc(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddControllers(options =>
            {
                options.Filters.Add(typeof(WebApiResultMiddleware));
            })
            //.AddNewtonsoftJson();//��ӻ��� Newtonsoft.Json �� JSON ��ʽ֧��
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            });
            services.AddCors(options =>
            {
                var origs = configuration.GetSection("AllowOrigins").Get<string[]>();
                if (origs != null)
                {
                    Startup.PolicyName = "with_origins";
                    options.AddPolicy(Startup.PolicyName, builder =>
                    {
                        builder.SetIsOriginAllowedToAllowWildcardSubdomains()
                        .WithOrigins(origs)
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                    });
                }

                options.AddPolicy("allow_all", builder =>
                {
                    builder.SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyOrigin() // �����κ���Դ����������
                    .AllowAnyMethod()
                    .AllowAnyHeader();
                    // .AllowCredentials();//ָ������cookie
                });
            });
            return services;
        }

        public static IServiceCollection AddHealthChecks(this IServiceCollection services, IConfiguration configuration)
        {
            var hcBuilder = services.AddHealthChecks();

            hcBuilder.AddCheck("self", () => HealthCheckResult.Healthy());
            //hcBuilder.AddSqlServer(configuration["ConnectionString"]);

            return services;
        }
        public static IServiceCollection AddCustomConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddOptions();
            services.Configure<AppSettings>(configuration);
           
            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc(Program.AppName, new OpenApiInfo
                {
                    Title = configuration["Title"],
                    Version = configuration["Version"],
                    Description = configuration["Description"]
                });

                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
            });

            return services;
        }
    }
}
