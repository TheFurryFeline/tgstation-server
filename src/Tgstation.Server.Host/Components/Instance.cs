using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octokit;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Host.Components.Byond;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Deployment;
using Tgstation.Server.Host.Components.Events;
using Tgstation.Server.Host.Components.Repository;
using Tgstation.Server.Host.Components.Watchdog;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Extensions;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Models;

namespace Tgstation.Server.Host.Components
{
	/// <inheritdoc />
	#pragma warning disable CA1506 // TODO: Decomplexify
	sealed class Instance : IInstance
	{
		/// <summary>
		/// Message for the <see cref="InvalidOperationException"/> if ever a job starts on a different <see cref="IInstanceCore"/> than the one that queued it.
		/// </summary>
		public const string DifferentCoreExceptionMessage = "Job started on different instance core!";

		/// <inheritdoc />
		public IRepositoryManager RepositoryManager { get; }

		/// <inheritdoc />
		public IByondManager ByondManager { get; }

		/// <inheritdoc />
		public IWatchdog Watchdog { get; }

		/// <inheritdoc />
		public IChatManager Chat { get; }

		/// <inheritdoc />
		public StaticFiles.IConfiguration Configuration { get; }

		/// <inheritdoc />
		public IDreamMaker DreamMaker { get; }

		/// <summary>
		/// The <see cref="IDmbFactory"/> for the <see cref="Instance"/>
		/// </summary>
		readonly IDmbFactory dmbFactory;

		/// <summary>
		/// The <see cref="IJobManager"/> for the <see cref="Instance"/>
		/// </summary>
		readonly IJobManager jobManager;

		/// <summary>
		/// The <see cref="IEventConsumer"/> for the <see cref="Instance"/>
		/// </summary>
		readonly IEventConsumer eventConsumer;

		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="Instance"/>.
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="Instance"/>
		/// </summary>
		readonly ILogger<Instance> logger;

		/// <summary>
		/// The <see cref="Api.Models.Instance"/> for the <see cref="Instance"/>
		/// </summary>
		readonly Api.Models.Instance metadata;

		/// <summary>
		/// The <see cref="GeneralConfiguration"/> for the <see cref="Instance"/>.
		/// </summary>
		readonly GeneralConfiguration generalConfiguration;

		/// <summary>
		/// <see langword="lock"/> <see cref="object"/> for <see cref="timerCts"/> and <see cref="timerTask"/>.
		/// </summary>
		readonly object timerLock;

		/// <summary>
		/// The auto update <see cref="Task"/>
		/// </summary>
		Task timerTask;

		/// <summary>
		/// <see cref="CancellationTokenSource"/> for <see cref="timerTask"/>
		/// </summary>
		CancellationTokenSource timerCts;

		/// <summary>
		/// Construct an <see cref="Instance"/>
		/// </summary>
		/// <param name="metadata">The value of <see cref="metadata"/></param>
		/// <param name="repositoryManager">The value of <see cref="RepositoryManager"/></param>
		/// <param name="byondManager">The value of <see cref="ByondManager"/></param>
		/// <param name="dreamMaker">The value of <see cref="DreamMaker"/></param>
		/// <param name="watchdog">The value of <see cref="Watchdog"/></param>
		/// <param name="chat">The value of <see cref="Chat"/></param>
		/// <param name="configuration">The value of <see cref="Configuration"/></param>
		/// <param name="dmbFactory">The value of <see cref="dmbFactory"/></param>
		/// <param name="jobManager">The value of <see cref="jobManager"/></param>
		/// <param name="eventConsumer">The value of <see cref="eventConsumer"/></param>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/>.</param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="generalConfiguration">The value of <see cref="generalConfiguration"/>.</param>
		public Instance(
			Api.Models.Instance metadata,
			IRepositoryManager repositoryManager,
			IByondManager byondManager,
			IDreamMaker dreamMaker,
			IWatchdog watchdog,
			IChatManager chat,
			StaticFiles.IConfiguration
			configuration,
			IDmbFactory dmbFactory,
			IJobManager jobManager,
			IEventConsumer eventConsumer,
			IGitHubClientFactory gitHubClientFactory,
			ILogger<Instance> logger,
			GeneralConfiguration generalConfiguration)
		{
			this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
			RepositoryManager = repositoryManager ?? throw new ArgumentNullException(nameof(repositoryManager));
			ByondManager = byondManager ?? throw new ArgumentNullException(nameof(byondManager));
			DreamMaker = dreamMaker ?? throw new ArgumentNullException(nameof(dreamMaker));
			Watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
			Chat = chat ?? throw new ArgumentNullException(nameof(chat));
			Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
			this.dmbFactory = dmbFactory ?? throw new ArgumentNullException(nameof(dmbFactory));
			this.jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
			this.eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.generalConfiguration = generalConfiguration ?? throw new ArgumentNullException(nameof(generalConfiguration));

			timerLock = new object();
		}

		/// <inheritdoc />
		public async ValueTask DisposeAsync()
		{
			using (LogContext.PushProperty("Instance", metadata.Id))
			{
				timerCts?.Dispose();
				Configuration.Dispose();
				await Chat.DisposeAsync().ConfigureAwait(false);
				await Watchdog.DisposeAsync().ConfigureAwait(false);
				dmbFactory.Dispose();
				RepositoryManager.Dispose();
			}
		}

		/// <summary>
		/// Pull the repository and compile for every set of given <paramref name="minutes"/>
		/// </summary>
		/// <param name="minutes">How many minutes the operation should repeat. Does not include running time</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		#pragma warning disable CA1502 // TODO: Decomplexify
		async Task TimerLoop(uint minutes, CancellationToken cancellationToken)
		{
			logger.LogDebug("Entering auto-update loop");
			while (true)
				try
				{
					await Task.Delay(TimeSpan.FromMinutes(minutes > Int32.MaxValue ? Int32.MaxValue : (int)minutes), cancellationToken).ConfigureAwait(false);
					logger.LogInformation("Beginning auto update...");
					await eventConsumer.HandleEvent(EventType.InstanceAutoUpdateStart, new List<string>(), cancellationToken).ConfigureAwait(false);
					try
					{
						var repositoryUpdateJob = new Job
						{
							Instance = new Models.Instance
							{
								Id = metadata.Id
							},
							Description = "Scheduled repository update",
							CancelRightsType = RightsType.Repository,
							CancelRight = (ulong)RepositoryRights.CancelPendingChanges
						};

						string deploySha = null;
						await jobManager.RegisterOperation(repositoryUpdateJob, async (core, databaseContextFactory, paramJob, progressReporter, jobCancellationToken) =>
						{
							if (core != this)
								throw new InvalidOperationException(DifferentCoreExceptionMessage);

							// assume 5 steps with synchronize
							const int ProgressSections = 7;
							const int ProgressStep = 100 / ProgressSections;
							string repoHead = null;

							await databaseContextFactory.UseContext(
								async databaseContext =>
								{
									var repositorySettingsTask = databaseContext
										.RepositorySettings
										.AsQueryable()
										.Where(x => x.InstanceId == metadata.Id)
										.FirstAsync(jobCancellationToken);

									const int NumSteps = 3;
									var doneSteps = 0;

									Action<int> NextProgressReporter()
									{
										var tmpDoneSteps = doneSteps;
										++doneSteps;
										return progress => progressReporter((progress + (100 * tmpDoneSteps)) / NumSteps);
									}

									using var repo = await RepositoryManager.LoadRepository(jobCancellationToken).ConfigureAwait(false);
									if (repo == null)
									{
										logger.LogTrace("Aborting repo update, no repository!");
										return;
									}

									var startSha = repo.Head;
									if (!repo.Tracking)
									{
										logger.LogTrace("Aborting repo update, active ref not tracking any remote branch!");
										deploySha = startSha;
										return;
									}

									var repositorySettings = await repositorySettingsTask.ConfigureAwait(false);

									// the main point of auto update is to pull the remote
									await repo.FetchOrigin(repositorySettings.AccessUser, repositorySettings.AccessToken, NextProgressReporter(), jobCancellationToken).ConfigureAwait(false);

									RevisionInformation currentRevInfo = null;
									bool hasDbChanges = false;

									Task<RevisionInformation> LoadRevInfo() => databaseContext.RevisionInformations
											.AsQueryable()
											.Where(x => x.CommitSha == startSha && x.Instance.Id == metadata.Id)
											.Include(x => x.ActiveTestMerges).ThenInclude(x => x.TestMerge)
											.FirstOrDefaultAsync(jobCancellationToken);

									async Task UpdateRevInfo(string currentHead, bool onOrigin, IEnumerable<RevInfoTestMerge> updatedTestMerges)
									{
										if (currentRevInfo == null)
											currentRevInfo = await LoadRevInfo().ConfigureAwait(false);

										if (currentRevInfo == default)
										{
											logger.LogInformation(Repository.Repository.OriginTrackingErrorTemplate, currentHead);
											onOrigin = true;
										}

										var attachedInstance = new Models.Instance
										{
											Id = metadata.Id
										};
										var oldRevInfo = currentRevInfo;
										currentRevInfo = new RevisionInformation
										{
											CommitSha = currentHead,
											OriginCommitSha = onOrigin ? currentHead : oldRevInfo.OriginCommitSha,
											Instance = attachedInstance
										};
										if (!onOrigin)
											currentRevInfo.ActiveTestMerges = new List<RevInfoTestMerge>(
												updatedTestMerges ?? oldRevInfo.ActiveTestMerges);

										databaseContext.Instances.Attach(attachedInstance);
										databaseContext.RevisionInformations.Add(currentRevInfo);
										hasDbChanges = true;
									}

									// take appropriate auto update actions
									bool shouldSyncTracked = false;
									bool preserveTestMerges = repositorySettings.AutoUpdatesKeepTestMerges.Value;
									if (preserveTestMerges)
									{
										logger.LogTrace("Preserving test merges...");

										var currentRevInfoTask = LoadRevInfo();

										var result = await repo.MergeOrigin(repositorySettings.CommitterName, repositorySettings.CommitterEmail, NextProgressReporter(), jobCancellationToken).ConfigureAwait(false);

										if (!result.HasValue)
											throw new JobException(Api.Models.ErrorCode.InstanceUpdateTestMergeConflict);

										currentRevInfo = await currentRevInfoTask.ConfigureAwait(false);

										var updatedTestMerges = await RemoveMergedPullRequests(
											repo,
											repositorySettings,
											currentRevInfo,
											cancellationToken)
										.ConfigureAwait(false);

										var lastRevInfoWasOriginCommit = currentRevInfo == default || currentRevInfo.CommitSha == currentRevInfo.OriginCommitSha;
										var stillOnOrigin = result.Value && lastRevInfoWasOriginCommit;

										var currentHead = repo.Head;
										if (currentHead != startSha)
										{
											await UpdateRevInfo(currentHead, stillOnOrigin, updatedTestMerges).ConfigureAwait(false);
											shouldSyncTracked = stillOnOrigin;
										}
										else
											shouldSyncTracked = false;
									}

									if (!preserveTestMerges)
									{
										logger.LogTrace("Resetting to origin...");
										await repo.ResetToOrigin(NextProgressReporter(), jobCancellationToken).ConfigureAwait(false);

										var currentHead = repo.Head;

										currentRevInfo = await databaseContext.RevisionInformations
											.AsQueryable()
											.Where(x => x.CommitSha == currentHead && x.Instance.Id == metadata.Id)
											.FirstOrDefaultAsync(jobCancellationToken)
											.ConfigureAwait(false);

										if (currentHead != startSha && currentRevInfo == default)
											await UpdateRevInfo(currentHead, true, null).ConfigureAwait(false);

										shouldSyncTracked = true;
									}

									// synch if necessary
									if (repositorySettings.AutoUpdatesSynchronize.Value && startSha != repo.Head)
									{
										var pushedOrigin = await repo.Sychronize(repositorySettings.AccessUser, repositorySettings.AccessToken, repositorySettings.CommitterName, repositorySettings.CommitterEmail, NextProgressReporter(), shouldSyncTracked, jobCancellationToken).ConfigureAwait(false);
										var currentHead = repo.Head;
										if (currentHead != currentRevInfo.CommitSha)
											await UpdateRevInfo(currentHead, pushedOrigin, null).ConfigureAwait(false);
									}

									repoHead = repo.Head;

									if (hasDbChanges)
										try
										{
											await databaseContext.Save(jobCancellationToken).ConfigureAwait(false);
										}
										catch
										{
											// DCT: Cancellation token is for job, operation must run regardless
											await repo.ResetToSha(startSha, progressReporter, default).ConfigureAwait(false);
											throw;
										}
								})
							.ConfigureAwait(false);

							progressReporter(5 * ProgressStep);
							deploySha = repoHead;
						}, cancellationToken).ConfigureAwait(false);

						// DCT: First token will cancel the job, second is for cancelling the cancellation, unwanted
						await jobManager.WaitForJobCompletion(repositoryUpdateJob, null, cancellationToken, default).ConfigureAwait(false);

						if (deploySha == null)
						{
							logger.LogTrace("Aborting auto update, repository error!");
							continue;
						}

						if(deploySha == LatestCompileJob()?.RevisionInformation.CommitSha)
						{
							logger.LogTrace("Aborting auto update, same revision as latest CompileJob");
							continue;
						}

						// finally set up the job
						var compileProcessJob = new Job
						{
							Instance = repositoryUpdateJob.Instance,
							Description = "Scheduled code deployment",
							CancelRightsType = RightsType.DreamMaker,
							CancelRight = (ulong)DreamMakerRights.CancelCompile
						};

						await jobManager.RegisterOperation(
							compileProcessJob,
							(core, databaseContextFactory, job, progressReporter, jobCancellationToken) =>
							{
								if (core != this)
									throw new InvalidOperationException(DifferentCoreExceptionMessage);
								return DreamMaker.DeploymentProcess(
									job,
									databaseContextFactory,
									progressReporter,
									jobCancellationToken);
							},
							cancellationToken).ConfigureAwait(false);

						await jobManager.WaitForJobCompletion(compileProcessJob, null, default, cancellationToken).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						logger.LogDebug("Cancelled auto update job!");
						throw;
					}
					catch (Exception e)
					{
						logger.LogWarning(e, "Error in auto update loop!");
						continue;
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}

			logger.LogTrace("Leaving auto update loop...");
		}
#pragma warning restore CA1502

		/// <summary>
		/// Get the updated list of <see cref="TestMerge"/>s for an origin merge.
		/// </summary>
		/// <param name="repository">The <see cref="IRepository"/> to use.</param>
		/// <param name="repositorySettings">The <see cref="RepositorySettings"/>.</param>
		/// <param name="revisionInformation">The current <see cref="RevisionInformation"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IReadOnlyCollection{T}"/> of <see cref="RevInfoTestMerge"/>s that should remain the new <see cref="RevisionInformation"/>.</returns>
		async Task<IReadOnlyCollection<RevInfoTestMerge>> RemoveMergedPullRequests(
			IRepository repository,
			RepositorySettings repositorySettings,
			RevisionInformation revisionInformation,
			CancellationToken cancellationToken)
		{
			if (revisionInformation.ActiveTestMerges?.Any() != true)
			{
				logger.LogTrace("No test merges to remove.");
				return Array.Empty<RevInfoTestMerge>();
			}

			var gitHubClient = repositorySettings.AccessToken != null
				? gitHubClientFactory.CreateClient(repositorySettings.AccessToken)
				: (String.IsNullOrEmpty(generalConfiguration.GitHubAccessToken)
					? gitHubClientFactory.CreateClient()
					: gitHubClientFactory.CreateClient(generalConfiguration.GitHubAccessToken));

			var tasks = revisionInformation
				.ActiveTestMerges
				.Select(x => gitHubClient
					.PullRequest
					.Get(repository.GitHubOwner, repository.GitHubRepoName, x.TestMerge.Number)
					.WithToken(cancellationToken));
			try
			{
				await Task.WhenAll(tasks).ConfigureAwait(false);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				logger.LogWarning(ex, "Pull requests update check failed!");
			}

			var newList = revisionInformation.ActiveTestMerges.ToList();

			async Task CheckRemovePR(Task<PullRequest> task)
			{
				var pr = await task.ConfigureAwait(false);
				if (!pr.Merged)
					return;

				// We don't just assume, actually check the repo contains the merge commit.
				if (await repository.ShaIsParent(pr.MergeCommitSha, cancellationToken).ConfigureAwait(false))
					newList.Remove(
						newList.First(
							potential => potential.TestMerge.Number == pr.Number));
			}

			foreach (var prTask in tasks)
				await CheckRemovePR(prTask).ConfigureAwait(false);

			return newList;
		}

		/// <inheritdoc />
		public Task InstanceRenamed(string newName, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(newName))
				throw new ArgumentNullException(nameof(newName));
			metadata.Name = newName;
			return Watchdog.InstanceRenamed(newName, cancellationToken);
		}

		/// <inheritdoc />
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			using (LogContext.PushProperty("Instance", metadata.Id))
			{
				await Task.WhenAll(
				SetAutoUpdateInterval(metadata.AutoUpdateInterval.Value),
				Configuration.StartAsync(cancellationToken),
				ByondManager.StartAsync(cancellationToken),
				Chat.StartAsync(cancellationToken),
				dmbFactory.StartAsync(cancellationToken))
				.ConfigureAwait(false);

				// dependent on so many things, its just safer this way
				await Watchdog.StartAsync(cancellationToken).ConfigureAwait(false);

				await dmbFactory.CleanUnusedCompileJobs(cancellationToken).ConfigureAwait(false);
			}
		}

		/// <inheritdoc />
		public async Task StopAsync(CancellationToken cancellationToken)
		{
			using (LogContext.PushProperty("Instance", metadata.Id))
			{
				await SetAutoUpdateInterval(0).ConfigureAwait(false);
				await Watchdog.StopAsync(cancellationToken).ConfigureAwait(false);
				await Task.WhenAll(
					Configuration.StopAsync(cancellationToken),
					ByondManager.StopAsync(cancellationToken),
					Chat.StopAsync(cancellationToken),
					dmbFactory.StopAsync(cancellationToken))
					.ConfigureAwait(false);
			}
		}

		/// <inheritdoc />
		public async Task SetAutoUpdateInterval(uint newInterval)
		{
			Task toWait;
			lock (timerLock)
			{
				if (timerTask != null)
				{
					logger.LogTrace("Cancelling auto-update task");
					timerCts.Cancel();
					timerCts.Dispose();
					toWait = timerTask;
					timerTask = null;
					timerCts = null;
				}
				else
					toWait = Task.CompletedTask;
			}

			await toWait.ConfigureAwait(false);
			if (newInterval == 0)
			{
				logger.LogTrace("New auto-update interval is 0. Not starting task.");
				return;
			}

			lock (timerLock)
			{
				// race condition, just quit
				if (timerTask != null)
				{
					logger.LogWarning("Aborting auto update interval change due to race condition!");
					return;
				}

				timerCts = new CancellationTokenSource();
				timerTask = TimerLoop(newInterval, timerCts.Token);
			}
		}

		/// <inheritdoc />
		public CompileJob LatestCompileJob() => dmbFactory.LatestCompileJob();
	}
}
