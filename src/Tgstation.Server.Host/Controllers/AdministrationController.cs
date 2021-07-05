﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Request;
using Tgstation.Server.Api.Models.Response;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.Extensions;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Security;
using Tgstation.Server.Host.System;
using Tgstation.Server.Host.Transfer;

namespace Tgstation.Server.Host.Controllers
{
	/// <summary>
	/// <see cref="ApiController"/> for TGS administration purposes.
	/// </summary>
	[Route(Routes.Administration)]
	public sealed class AdministrationController : ApiController
	{
		/// <summary>
		/// Default <see cref="Exception.Message"/> for <see cref="ApiException"/>s.
		/// </summary>
		const string OctokitException = "Bad GitHub API response, check configuration!";

		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="IServerControl"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IServerControl serverControl;

		/// <summary>
		/// The <see cref="IServerUpdateInitiator"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IServerUpdateInitiator serverUpdater;

		/// <summary>
		/// The <see cref="IAssemblyInformationProvider"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IAssemblyInformationProvider assemblyInformationProvider;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// The <see cref="IFileTransferTicketProvider"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly IFileTransferTicketProvider fileTransferService;

		/// <summary>
		/// The <see cref="UpdatesConfiguration"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly UpdatesConfiguration updatesConfiguration;

		/// <summary>
		/// The <see cref="FileLoggingConfiguration"/> for the <see cref="AdministrationController"/>.
		/// </summary>
		readonly FileLoggingConfiguration fileLoggingConfiguration;

		/// <summary>
		/// Initializes a new instance of the <see cref="AdministrationController"/> class.
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the <see cref="ApiController"/>.</param>
		/// <param name="authenticationContextFactory">The <see cref="IAuthenticationContextFactory"/> for the <see cref="ApiController"/>.</param>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/>.</param>
		/// <param name="serverControl">The value of <see cref="serverControl"/>.</param>
		/// <param name="serverUpdater">The value of <see cref="serverUpdater"/>.</param>
		/// <param name="assemblyInformationProvider">The value of <see cref="assemblyInformationProvider"/>.</param>
		/// <param name="ioManager">The value of <see cref="ioManager"/>.</param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/>.</param>
		/// <param name="fileTransferService">The value of <see cref="fileTransferService"/>.</param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="ApiController"/>.</param>
		/// <param name="updatesConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing value of <see cref="updatesConfiguration"/>.</param>
		/// <param name="fileLoggingConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing value of <see cref="fileLoggingConfiguration"/>.</param>
		public AdministrationController(
			IDatabaseContext databaseContext,
			IAuthenticationContextFactory authenticationContextFactory,
			IGitHubClientFactory gitHubClientFactory,
			IServerControl serverControl,
			IServerUpdateInitiator serverUpdater,
			IAssemblyInformationProvider assemblyInformationProvider,
			IIOManager ioManager,
			IPlatformIdentifier platformIdentifier,
			IFileTransferTicketProvider fileTransferService,
			ILogger<AdministrationController> logger,
			IOptions<UpdatesConfiguration> updatesConfigurationOptions,
			IOptions<FileLoggingConfiguration> fileLoggingConfigurationOptions)
			: base(
				databaseContext,
				authenticationContextFactory,
				logger,
				true)
		{
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.serverControl = serverControl ?? throw new ArgumentNullException(nameof(serverControl));
			this.serverUpdater = serverUpdater ?? throw new ArgumentNullException(nameof(serverUpdater));
			this.assemblyInformationProvider = assemblyInformationProvider ?? throw new ArgumentNullException(nameof(assemblyInformationProvider));
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
			this.fileTransferService = fileTransferService ?? throw new ArgumentNullException(nameof(fileTransferService));
			updatesConfiguration = updatesConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(updatesConfigurationOptions));
			fileLoggingConfiguration = fileLoggingConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(fileLoggingConfigurationOptions));
		}

		/// <summary>
		/// Get <see cref="AdministrationResponse"/> server information.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> for the operation.</returns>
		/// <response code="200">Retrieved <see cref="AdministrationResponse"/> data successfully.</response>
		/// <response code="424">The GitHub API rate limit was hit. See response header Retry-After.</response>
		/// <response code="429">A GitHub API error occurred. See error message for details.</response>
		[HttpGet]
		[TgsAuthorize(AdministrationRights.ChangeVersion)]
		[ProducesResponseType(typeof(AdministrationResponse), 200)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 424)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 429)]
		public async Task<IActionResult> Read(CancellationToken cancellationToken)
		{
			try
			{
				Version greatestVersion = null;
				Uri repoUrl = null;
				try
				{
					var gitHubClient = gitHubClientFactory.CreateClient();
					var repositoryTask = gitHubClient
						.Repository
						.Get(updatesConfiguration.GitHubRepositoryId)
						.WithToken(cancellationToken);
					var releases = (await gitHubClient
						.Repository
						.Release
						.GetAll(updatesConfiguration.GitHubRepositoryId)
						.WithToken(cancellationToken)
						.ConfigureAwait(false))
						.Where(x => x.TagName.StartsWith(
							updatesConfiguration.GitTagPrefix,
							StringComparison.InvariantCulture));

					foreach (var release in releases)
						if (Version.TryParse(release.TagName.Replace(updatesConfiguration.GitTagPrefix, String.Empty, StringComparison.Ordinal), out var version)
							&& version.Major == assemblyInformationProvider.Version.Major
							&& (greatestVersion == null || version > greatestVersion))
							greatestVersion = version;
					repoUrl = new Uri((await repositoryTask.ConfigureAwait(false)).HtmlUrl);
				}
				catch (NotFoundException e)
				{
					Logger.LogWarning(e, "Not found exception while retrieving upstream repository info!");
				}

				return Json(new AdministrationResponse
				{
					LatestVersion = greatestVersion,
					TrackedRepositoryUrl = repoUrl,
				});
			}
			catch (RateLimitExceededException e)
			{
				return RateLimit(e);
			}
			catch (ApiException e)
			{
				Logger.LogWarning(e, OctokitException);
				return StatusCode(HttpStatusCode.FailedDependency, new ErrorMessageResponse(ErrorCode.RemoteApiError)
				{
					AdditionalData = e.Message,
				});
			}
		}

		/// <summary>
		/// Attempt to perform a server upgrade.
		/// </summary>
		/// <param name="model">The <see cref="ServerUpdateRequest"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> for the operation.</returns>
		/// <response code="202">Update has been started successfully.</response>
		/// <response code="410">The requested release version could not be found in the target GitHub repository.</response>
		/// <response code="422">Upgrade operations are unavailable due to the launch configuration of TGS.</response>
		/// <response code="424">A GitHub rate limit was encountered.</response>
		/// <response code="429">A GitHub API error occurred.</response>
		[HttpPost]
		[TgsAuthorize(AdministrationRights.ChangeVersion)]
		[ProducesResponseType(typeof(ServerUpdateResponse), 202)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 410)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 422)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 424)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 429)]
		public async Task<IActionResult> Update([FromBody] ServerUpdateRequest model, CancellationToken cancellationToken)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			if (model.NewVersion == null)
				return BadRequest(new ErrorMessageResponse(ErrorCode.ModelValidationFailure)
				{
					AdditionalData = "newVersion is required!",
				});

			if (model.NewVersion.Major != assemblyInformationProvider.Version.Major)
				return BadRequest(new ErrorMessageResponse(ErrorCode.CannotChangeServerSuite));

			if (!serverControl.WatchdogPresent)
				return UnprocessableEntity(new ErrorMessageResponse(ErrorCode.MissingHostWatchdog));

			try
			{
				var updateResult = await serverUpdater.BeginUpdate(model.NewVersion, cancellationToken).ConfigureAwait(false);
				if (updateResult == ServerUpdateResult.ReleaseMissing)
					return Gone();

				if (updateResult == ServerUpdateResult.UpdateInProgress)
					return BadRequest(new ErrorMessageResponse(ErrorCode.ServerUpdateInProgress));

				return Accepted(new ServerUpdateResponse
				{
					NewVersion = model.NewVersion,
				});
			}
			catch (RateLimitExceededException e)
			{
				return RateLimit(e);
			}
			catch (ApiException e)
			{
				Logger.LogWarning(e, OctokitException);
				return StatusCode(HttpStatusCode.FailedDependency, new ErrorMessageResponse(ErrorCode.RemoteApiError)
				{
					AdditionalData = e.Message,
				});
			}
		}

		/// <summary>
		/// Attempts to restart the server.
		/// </summary>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="204">Restart begun successfully.</response>
		/// <response code="422">Restart operations are unavailable due to the launch configuration of TGS.</response>
		[HttpDelete]
		[TgsAuthorize(AdministrationRights.RestartHost)]
		[ProducesResponseType(204)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 422)]
		public async Task<IActionResult> Delete()
		{
			try
			{
				if (!serverControl.WatchdogPresent)
				{
					Logger.LogDebug("Restart request failed due to lack of host watchdog!");
					return UnprocessableEntity(new ErrorMessageResponse(ErrorCode.MissingHostWatchdog));
				}

				await serverControl.Restart().ConfigureAwait(false);
				return NoContent();
			}
			catch (InvalidOperationException)
			{
				return StatusCode(HttpStatusCode.ServiceUnavailable);
			}
		}

		/// <summary>
		/// List <see cref="LogFileResponse"/>s present.
		/// </summary>
		/// <param name="page">The current page.</param>
		/// <param name="pageSize">The page size.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Listed logs successfully.</response>
		/// <response code="409">An IO error occurred while listing.</response>
		[HttpGet(Routes.Logs)]
		[TgsAuthorize(AdministrationRights.DownloadLogs)]
		[ProducesResponseType(typeof(PaginatedResponse<LogFileResponse>), 200)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 409)]
		public Task<IActionResult> ListLogs([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
			=> Paginated(
				async () =>
				{
					var path = fileLoggingConfiguration.GetFullLogDirectory(ioManager, assemblyInformationProvider, platformIdentifier);
					try
					{
						var files = await ioManager.GetFiles(path, cancellationToken).ConfigureAwait(false);
						var tasks = files.Select(
							async file => new LogFileResponse
							{
								Name = ioManager.GetFileName(file),
								LastModified = await ioManager
									.GetLastModified(
										ioManager.ConcatPath(path, file),
										cancellationToken)
										.ConfigureAwait(false),
							})
							.ToList();

						await Task.WhenAll(tasks).ConfigureAwait(false);

						return new PaginatableResult<LogFileResponse>(
							tasks
								.AsQueryable()
								.Select(x => x.Result)
								.OrderByDescending(x => x.Name));
					}
					catch (IOException ex)
					{
						return new PaginatableResult<LogFileResponse>(
							Conflict(new ErrorMessageResponse(ErrorCode.IOError)
							{
								AdditionalData = ex.ToString(),
							}));
					}
				},
				null,
				page,
				pageSize,
				cancellationToken);

		/// <summary>
		/// Download a <see cref="LogFileResponse"/>.
		/// </summary>
		/// <param name="path">The path to download.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Downloaded <see cref="LogFileResponse"/> successfully.</response>
		/// <response code="409">An IO error occurred while downloading.</response>
		[HttpGet(Routes.Logs + "/{*path}")]
		[TgsAuthorize(AdministrationRights.DownloadLogs)]
		[ProducesResponseType(typeof(LogFileResponse), 200)]
		[ProducesResponseType(typeof(ErrorMessageResponse), 409)]
		public async Task<IActionResult> GetLog(string path, CancellationToken cancellationToken)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));

			path = HttpUtility.UrlDecode(path);

			// guard against directory navigation
			var sanitizedPath = ioManager.GetFileName(path);
			if (path != sanitizedPath)
				return Forbid();

			var fullPath = ioManager.ConcatPath(
				fileLoggingConfiguration.GetFullLogDirectory(ioManager, assemblyInformationProvider, platformIdentifier),
				path);
			try
			{
				var fileTransferTicket = fileTransferService.CreateDownload(
					new FileDownloadProvider(
						() => null,
						null,
						fullPath,
						true));

				var readTask = ioManager.ReadAllBytes(fullPath, cancellationToken);

				return Ok(new LogFileResponse
				{
					Name = path,
					LastModified = await ioManager.GetLastModified(fullPath, cancellationToken).ConfigureAwait(false),
					FileTicket = fileTransferTicket.FileTicket,
				});
			}
			catch (IOException ex)
			{
				return Conflict(new ErrorMessageResponse(ErrorCode.IOError)
				{
					AdditionalData = ex.ToString(),
				});
			}
		}
	}
}
