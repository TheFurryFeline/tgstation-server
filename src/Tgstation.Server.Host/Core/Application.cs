﻿using Cyberboss.AspNetCore.AsyncInitializer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;

namespace Tgstation.Server.Host.Core
{
    /// <summary>
    /// Configures the ASP.NET Core web application
    /// </summary>
	sealed class Application
	{
		/// <summary>
		/// The <see cref="IConfiguration"/> for the <see cref="Application"/>
		/// </summary>
		readonly IConfiguration configuration;

		/// <summary>
		/// The <see cref="IHostingEnvironment"/> for the <see cref="Application"/>
		/// </summary>
		readonly IHostingEnvironment hostingEnvironment;

		/// <summary>
		/// Construct an <see cref="Application"/>
		/// </summary>
		/// <param name="configuration">The value of <see cref="configuration"/></param>
		/// <param name="hostingEnvironment">The value of <see cref="hostingEnvironment"/></param>
		public Application(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
		{
			this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this.hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));
		}

		/// <summary>
		/// Configure dependency injected services
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/> to configure</param>
#pragma warning disable CA1822 // Mark members as static
		public void ConfigureServices(IServiceCollection services)
#pragma warning restore CA1822 // Mark members as static
		{
			if (services == null)
				throw new ArgumentNullException(nameof(services));
			var workingDir = Environment.CurrentDirectory;
			var databaseConfigurationSection = configuration.GetSection(DatabaseConfiguration.Section);
			services.Configure<DatabaseConfiguration>(databaseConfigurationSection);

            services.AddMvc();
            services.AddOptions();
			
			var databaseConfiguration = databaseConfigurationSection.Get<DatabaseConfiguration>();
			void ConfigureDatabase(DbContextOptionsBuilder builder)
			{
				if (hostingEnvironment.IsDevelopment())
					builder.EnableSensitiveDataLogging();
			};

			switch (databaseConfiguration.DatabaseType)
			{
				case DatabaseType.MySql:
					services.AddDbContext<MySqlDatabaseContext>(ConfigureDatabase);
					services.AddScoped<IDatabaseContext>(x => x.GetRequiredService<MySqlDatabaseContext>());
					break;
				case DatabaseType.Sqlite:
					services.AddDbContext<SqliteDatabaseContext>(ConfigureDatabase);
					services.AddScoped<IDatabaseContext>(x => x.GetRequiredService<SqliteDatabaseContext>());
					break;
				case DatabaseType.SqlServer:
					services.AddDbContext<SqlServerDatabaseContext>(ConfigureDatabase);
					services.AddScoped<IDatabaseContext>(x => x.GetRequiredService<SqlServerDatabaseContext>());
					break;
				default:
					throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Invalid {0}!", nameof(DatabaseType)));
			}
			
			services.AddSingleton<ICryptographySuite, CryptographySuite>();
			services.AddSingleton<IDatabaseSeeder, DatabaseSeeder>();
			services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
		}

		/// <summary>
		/// Configure the <see cref="Application"/>
		/// </summary>
		/// <param name="applicationBuilder">The <see cref="IApplicationBuilder"/> to configure</param>
		public void Configure(IApplicationBuilder applicationBuilder)
		{
			if (applicationBuilder == null)
				throw new ArgumentNullException(nameof(applicationBuilder));

			if (hostingEnvironment.IsDevelopment())
				applicationBuilder.UseDeveloperExceptionPage();

			applicationBuilder.UseAsyncInitialization(async (cancellationToken) =>
			{
				using (var scope = applicationBuilder.ApplicationServices.CreateScope())
					await scope.ServiceProvider.GetRequiredService<IDatabaseContext>().Initialize(cancellationToken).ConfigureAwait(false);
			});

			applicationBuilder.UseAuthentication();

			applicationBuilder.UseMvc();
		}
	}
}