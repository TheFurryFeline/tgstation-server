﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components.Chat.Providers
{
	/// <summary>
	/// <see cref="IProvider"/> for the Discord app.
	/// </summary>
	sealed class DiscordProvider : Provider
	{
		/// <inheritdoc />
		public override bool Connected => client.ConnectionState != ConnectionState.Disconnected;

		/// <inheritdoc />
		public override string BotMention
		{
			get
			{
				if (!Connected)
					throw new InvalidOperationException("Provider not connected");
				return NormalizeMentions(client.CurrentUser.Mention);
			}
		}

		/// <summary>
		/// The <see cref="IAssemblyInformationProvider"/> for the <see cref="DiscordProvider"/>.
		/// </summary>
		readonly IAssemblyInformationProvider assemblyInformationProvider;

		/// <summary>
		/// The <see cref="DiscordSocketClient"/> for the <see cref="DiscordProvider"/>.
		/// </summary>
		readonly DiscordSocketClient client;

		/// <summary>
		/// <see cref="List{T}"/> of mapped <see cref="ITextChannel"/> <see cref="IEntity{TId}.Id"/>s.
		/// </summary>
		readonly List<ulong> mappedChannels;

		/// <summary>
		/// The Discord bot token.
		/// </summary>
		readonly string botToken;

		/// <summary>
		/// <see cref="bool"/> to enable based mode. Will auto reply with a youtube link to a video that says "based on the hardware that's installed in it" to anyone saying 'based on what?' case-insensitive.
		/// </summary>
		readonly bool basedMeme;

		/// <summary>
		/// The <see cref="DiscordDMOutputDisplayType"/>.
		/// </summary>
		readonly DiscordDMOutputDisplayType outputDisplayType;

		/// <summary>
		/// Normalize a discord mention string.
		/// </summary>
		/// <param name="fromDiscord">The mention <see cref="string"/> provided by the Discord library.</param>
		/// <returns>The normalized mention <see cref="string"/>.</returns>
		static string NormalizeMentions(string fromDiscord) => fromDiscord.Replace("<@!", "<@", StringComparison.Ordinal);

		/// <summary>
		/// Create a <see cref="List{T}"/> of <see cref="EmbedFieldBuilder"/>s for a discord update embed.
		/// </summary>
		/// <param name="revisionInformation">The <see cref="RevisionInformation"/> of the deployment.</param>
		/// <param name="byondVersion">The BYOND <see cref="Version"/> of the deployment.</param>
		/// <param name="gitHubOwner">The repository GitHub owner, if any.</param>
		/// <param name="gitHubRepo">The repository GitHub name, if any.</param>
		/// <param name="localCommitPushed"><see langword="true"/> if the local deployment commit was pushed to the remote repository.</param>
		/// <returns>A new <see cref="List{T}"/> of <see cref="EmbedFieldBuilder"/>s to use.</returns>
		static List<EmbedFieldBuilder> BuildUpdateEmbedFields(
			Models.RevisionInformation revisionInformation,
			Version byondVersion,
			string gitHubOwner,
			string gitHubRepo,
			bool localCommitPushed)
		{
			bool gitHub = gitHubOwner != null && gitHubRepo != null;
			var fields = new List<EmbedFieldBuilder>
			{
				new EmbedFieldBuilder
				{
					Name = "BYOND Version",
					Value = $"{byondVersion.Major}.{byondVersion.Minor}{(byondVersion.Build > 0 ? $".{byondVersion.Build}" : String.Empty)}",
					IsInline = true,
				},
				new EmbedFieldBuilder
				{
					Name = "Local Commit",
					Value = localCommitPushed && gitHub
						? $"[{revisionInformation.CommitSha.Substring(0, 7)}](https://github.com/{gitHubOwner}/{gitHubRepo}/commit/{revisionInformation.CommitSha})"
						: revisionInformation.CommitSha.Substring(0, 7),
					IsInline = true,
				},
				new EmbedFieldBuilder
				{
					Name = "Branch Commit",
					Value = gitHub
						? $"[{revisionInformation.OriginCommitSha.Substring(0, 7)}](https://github.com/{gitHubOwner}/{gitHubRepo}/commit/{revisionInformation.OriginCommitSha})"
						: revisionInformation.OriginCommitSha.Substring(0, 7),
					IsInline = true,
				},
			};

			fields.AddRange((revisionInformation.ActiveTestMerges ?? Enumerable.Empty<RevInfoTestMerge>())
				.Select(x => x.TestMerge)
				.Select(x => new EmbedFieldBuilder
				{
					Name = $"#{x.Number}",
					Value = $"[{x.TitleAtMerge}]({x.Url}) by _[@{x.Author}](https://github.com/{x.Author})_{Environment.NewLine}Commit: [{x.TargetCommitSha.Substring(0, 7)}](https://github.com/{gitHubOwner}/{gitHubRepo}/commit/{x.TargetCommitSha}){(String.IsNullOrWhiteSpace(x.Comment) ? String.Empty : $"{Environment.NewLine}_**{x.Comment}**_")}",
				}));

			return fields;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DiscordProvider"/> class.
		/// </summary>
		/// <param name="jobManager">The <see cref="IJobManager"/> for the <see cref="Provider"/>.</param>
		/// <param name="assemblyInformationProvider">The value of <see cref="assemblyInformationProvider"/>.</param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="Provider"/>.</param>
		/// <param name="chatBot">The <see cref="ChatBot"/> for the <see cref="Provider"/>.</param>
		public DiscordProvider(
			IJobManager jobManager,
			IAssemblyInformationProvider assemblyInformationProvider,
			ILogger<DiscordProvider> logger,
			ChatBot chatBot)
			: base(jobManager, logger, chatBot)
		{
			this.assemblyInformationProvider = assemblyInformationProvider ?? throw new ArgumentNullException(nameof(assemblyInformationProvider));

			var csb = new DiscordConnectionStringBuilder(chatBot.ConnectionString);
			botToken = csb.BotToken;
			basedMeme = csb.BasedMeme;
			outputDisplayType = csb.DMOutputDisplay;

			client = new DiscordSocketClient();
			client.MessageReceived += Client_MessageReceived;
			mappedChannels = new List<ulong>();
		}

		/// <inheritdoc />
		public override async ValueTask DisposeAsync()
		{
			await base.DisposeAsync().ConfigureAwait(false);
			client.Dispose();
		}

		/// <inheritdoc />
		public override Task<IReadOnlyCollection<ChannelRepresentation>> MapChannels(IEnumerable<Api.Models.ChatChannel> channels, CancellationToken cancellationToken)
		{
			if (channels == null)
				throw new ArgumentNullException(nameof(channels));

			if (!Connected)
			{
				Logger.LogWarning("Cannot map channels, provider disconnected!");
				return Task.FromResult<IReadOnlyCollection<ChannelRepresentation>>(Array.Empty<ChannelRepresentation>());
			}

			ChannelRepresentation GetModelChannelFromDBChannel(Api.Models.ChatChannel channelFromDB)
			{
				if (!channelFromDB.DiscordChannelId.HasValue)
					throw new InvalidOperationException("ChatChannel missing DiscordChannelId!");

				var channelId = channelFromDB.DiscordChannelId.Value;
				ulong discordChannelId;
				string connectionName;
				string friendlyName;
				if (channelId == 0)
				{
					connectionName = client.CurrentUser.Username;
					friendlyName = "(Unmapped accessible channels)";
					discordChannelId = 0;
				}
				else
				{
					var discordChannel = client.GetChannel(channelId);
					if (!(discordChannel is ITextChannel textChannel))
					{
						Logger.LogWarning("Cound not map channel {0}! Incorrect type: {1}", channelId, discordChannel?.GetType());
						return null;
					}

					discordChannelId = textChannel.Id;
					connectionName = textChannel.Guild.Name;
					friendlyName = textChannel.Name;
				}

				var channelModel = new ChannelRepresentation
				{
					RealId = discordChannelId,
					IsAdminChannel = channelFromDB.IsAdminChannel == true,
					ConnectionName = connectionName,
					FriendlyName = friendlyName,
					IsPrivateChannel = false,
					Tag = channelFromDB.Tag,
				};
				Logger.LogTrace("Mapped channel {0}: {1}", channelModel.RealId, channelModel.FriendlyName);
				return channelModel;
			}

			var enumerator = channels
				.Select(x => GetModelChannelFromDBChannel(x))
				.Where(x => x != null).ToList();

			lock (mappedChannels)
			{
				mappedChannels.Clear();
				mappedChannels.AddRange(enumerator.Select(x => x.RealId));
			}

			return Task.FromResult<IReadOnlyCollection<ChannelRepresentation>>(enumerator);
		}

		/// <inheritdoc />
		public override async Task SendMessage(ulong channelId, string message, CancellationToken cancellationToken)
		{
			var requestOptions = new RequestOptions
			{
				CancelToken = cancellationToken,
				Timeout = 10000, // prevent stupid long hold ups from this
			};

			Task SendToChannel(IMessageChannel channel) => channel.SendMessageAsync(
				message,
				false,
				null,
				requestOptions);

			try
			{
				if (channelId == 0)
				{
					var unmappedTextChannels = client
						.Guilds
						.SelectMany(x => x.TextChannels);

					lock (mappedChannels)
						unmappedTextChannels = unmappedTextChannels.Where(x => !mappedChannels.Contains(x.Id));

					// discord API confirmed weak boned: https://stackoverflow.com/a/52462336
					var channelCount = 0UL;
					var tasks = unmappedTextChannels
						.Select(x =>
						{
							++channelCount;
							return SendToChannel(x);
						});

					if (channelCount > 0)
					{
						Logger.LogTrace("Dispatched to {0} unmapped channels...", channelCount);
						await Task.WhenAll(tasks).ConfigureAwait(false);
					}

					return;
				}

				if (!(client.GetChannel(channelId) is IMessageChannel channel))
					return;

				await SendToChannel(channel).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				if (e is OperationCanceledException)
					cancellationToken.ThrowIfCancellationRequested();
				Logger.LogWarning(e, "Error sending discord message!");
			}
		}

		/// <inheritdoc />
		public override async Task<Func<string, string, Task>> SendUpdateMessage(
			Models.RevisionInformation revisionInformation,
			Version byondVersion,
			DateTimeOffset? estimatedCompletionTime,
			string gitHubOwner,
			string gitHubRepo,
			ulong channelId,
			bool localCommitPushed,
			CancellationToken cancellationToken)
		{
			localCommitPushed |= revisionInformation.CommitSha == revisionInformation.OriginCommitSha;

			var fields = BuildUpdateEmbedFields(revisionInformation, byondVersion, gitHubOwner, gitHubRepo, localCommitPushed);
			var builder = new EmbedBuilder
			{
				Author = new EmbedAuthorBuilder
				{
					Name = assemblyInformationProvider.VersionPrefix,
					Url = "https://github.com/tgstation/tgstation-server",
					IconUrl = "https://avatars0.githubusercontent.com/u/1363778?s=280&v=4",
				},
				Color = Color.Gold,
				Description = "TGS has begun deploying active repository code to production.",
				Fields = fields,
				Title = "Code Deployment",
				Footer = new EmbedFooterBuilder
				{
					Text = $"In progress...{(estimatedCompletionTime.HasValue ? " ETA" : String.Empty)}",
				},
				Timestamp = estimatedCompletionTime,
			};

			Logger.LogTrace("Attempting to post deploy embed to channel {0}...", channelId);
			if (!(client.GetChannel(channelId) is IMessageChannel channel))
			{
				Logger.LogTrace("Channel ID {0} does not exist or is not an IMessageChannel!", channelId);
				return (errorMessage, dreamMakerOutput) => Task.CompletedTask;
			}

			var message = await channel.SendMessageAsync(
				"DM: Deployment in Progress...",
				false,
				builder.Build(),
				new RequestOptions
				{
					CancelToken = cancellationToken,
				})
				.ConfigureAwait(false);

			return async (errorMessage, dreamMakerOutput) =>
			{
				var completionString = errorMessage == null ? "Succeeded" : "Failed";
				builder.Footer.Text = completionString;
				builder.Color = errorMessage == null ? Color.Green : Color.Red;
				builder.Timestamp = DateTimeOffset.UtcNow;
				builder.Description = errorMessage == null
					? "The deployment completed successfully and will be available at the next server reboot."
					: "The deployment failed.";

				var showDMOutput = outputDisplayType switch
				{
					DiscordDMOutputDisplayType.Always => true,
					DiscordDMOutputDisplayType.Never => false,
					DiscordDMOutputDisplayType.OnError => errorMessage != null,
					_ => throw new InvalidOperationException($"Invalid DiscordDMOutputDisplayType: {outputDisplayType}"),
				};

				if (dreamMakerOutput != null)
				{
					showDMOutput = showDMOutput && dreamMakerOutput.Length < EmbedFieldBuilder.MaxFieldValueLength - (6 + Environment.NewLine.Length);
					if (showDMOutput)
						builder.AddField(new EmbedFieldBuilder
						{
							Name = "DreamMaker Output",
							Value = $"```{Environment.NewLine}{dreamMakerOutput}{Environment.NewLine}```",
						});
				}

				if (errorMessage != null)
					builder.AddField(new EmbedFieldBuilder
					{
						Name = "Error Message",
						Value = errorMessage,
					});

				var updatedMessage = $"DM: Deployment {completionString}!";
				try
				{
					await message.ModifyAsync(
						props =>
						{
							props.Content = updatedMessage;
							props.Embed = builder.Build();
						})
						.ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex, "Updating deploy embed {0} failed, attempting new post!", message.Id);
					try
					{
						await channel.SendMessageAsync(
							updatedMessage,
							false,
							builder.Build())
							.ConfigureAwait(false);
					}
					catch (Exception ex2)
					{
						Logger.LogWarning(ex2, "Posting completion deploy embed failed!");
					}
				}
			};
		}

		/// <inheritdoc />
		protected override async Task Connect(CancellationToken cancellationToken)
		{
			try
			{
				await client.LoginAsync(TokenType.Bot, botToken, true).ConfigureAwait(false);

				Logger.LogTrace("Logged in.");
				cancellationToken.ThrowIfCancellationRequested();

				var channelsAvailable = new TaskCompletionSource<object>();
				Task ReadyCallback()
				{
					channelsAvailable.TrySetResult(null);
					return Task.CompletedTask;
				}

				client.Ready += ReadyCallback;
				try
				{
					await client.StartAsync().ConfigureAwait(false);

					Logger.LogTrace("Started.");

					using (cancellationToken.Register(() => channelsAvailable.SetCanceled()))
						await channelsAvailable.Task.ConfigureAwait(false);
				}
				finally
				{
					client.Ready -= ReadyCallback;
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				throw new JobException(ErrorCode.ChatCannotConnectProvider, e);
			}
		}

		/// <inheritdoc />
		protected override async Task DisconnectImpl(CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var disconnectTcs = new TaskCompletionSource<object>();
				Task DisconnectCallback(Exception exception)
				{
					if (exception != null)
						Logger.LogTrace(exception, "Error stopping discord client!");

					disconnectTcs.TrySetResult(null);
					return Task.CompletedTask;
				}

				try
				{
					client.Disconnected += DisconnectCallback;

					await client.StopAsync().ConfigureAwait(false);

					Logger.LogTrace("Waiting for disconnect callback...");
					using (cancellationToken.Register(() => disconnectTcs.SetCanceled()))
						await disconnectTcs.Task.ConfigureAwait(false);

					// https://github.com/discord-net/Discord.Net/blob/8afef8245cfd1f8b56956dd4b4577ed3c6904be5/src/Discord.Net.WebSocket/ConnectionManager.cs#L176
					// State isn't set to disconnected until AFTER the callback fires
					// Meaning if we check this.Connected right now it will still return true
					// Yielding here will prevent this
					await Task.Yield();

					Logger.LogTrace("Stop async complete.");
				}
				finally
				{
					client.Disconnected -= DisconnectCallback;
				}

				cancellationToken.ThrowIfCancellationRequested();
				var logoutTcs = new TaskCompletionSource<object>();
				Task LogoutCallback()
				{
					logoutTcs.TrySetResult(null);
					return Task.CompletedTask;
				}

				client.LoggedOut += LogoutCallback;
				try
				{
					await client.LogoutAsync().ConfigureAwait(false);

					Logger.LogTrace("Waiting for logout callback...");
					using (cancellationToken.Register(() => logoutTcs.SetCanceled()))
						await logoutTcs.Task.ConfigureAwait(false);
				}
				finally
				{
					client.LoggedOut -= LogoutCallback;
				}

				Logger.LogDebug("Disconnected!");
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Logger.LogWarning(e, "Error disconnecting from discord!");
			}
		}

		/// <summary>
		/// Handle a message recieved from Discord.
		/// </summary>
		/// <param name="e">The <see cref="SocketMessage"/>.</param>
		/// <returns>A <see cref="Task"/> representing the running operation.</returns>
		async Task Client_MessageReceived(SocketMessage e)
		{
			if (e.Author.Id == client.CurrentUser.Id)
				return;

			IDisposable typingState = null;
			void StartTyping() => typingState = e.Channel.EnterTypingState();
			try
			{
				if (basedMeme && e.Content.Equals("Based on what?", StringComparison.OrdinalIgnoreCase))
				{
					StartTyping();

					// DCT: None available
					await SendMessage(
						e.Channel.Id,
						"https://youtu.be/LrNu-SuFF_o",
						default)
						.ConfigureAwait(false);
					return;
				}

				var pm = e.Channel is IPrivateChannel;
				var shouldNotAnswer = !pm;
				if (shouldNotAnswer)
					lock (mappedChannels)
						shouldNotAnswer = !mappedChannels.Contains(e.Channel.Id);

				var content = NormalizeMentions(e.Content);
				var mentionedUs = e.MentionedUsers.Any(x => x.Id == client.CurrentUser.Id)
					|| (!shouldNotAnswer && content.Split(' ').First().Equals(ChatManager.CommonMention, StringComparison.OrdinalIgnoreCase));
				if (mentionedUs)
					StartTyping();

				if (shouldNotAnswer)
				{
					if (mentionedUs)
					{
						Logger.LogTrace("Ignoring mention from {0} ({1}) by {2} ({3}). Channel not mapped!", e.Channel.Id, e.Channel.Name, e.Author.Id, e.Author.Username);

						// DCT: None available
						await SendMessage(e.Channel.Id, "I do not respond to this channel!", default).ConfigureAwait(false);
					}

					return;
				}

				var result = new Message
				{
					Content = content,
					User = new ChatUser
					{
						RealId = e.Author.Id,
						Channel = new ChannelRepresentation
						{
							RealId = e.Channel.Id,
							IsPrivateChannel = pm,
							ConnectionName = pm ? e.Author.Username : (e.Channel as ITextChannel)?.Guild.Name ?? "UNKNOWN",
							FriendlyName = e.Channel.Name,

							// isAdmin and Tag populated by manager
						},
						FriendlyName = e.Author.Username,
						Mention = NormalizeMentions(e.Author.Mention),
					},
					Context = typingState,
				};

				EnqueueMessage(result);
				typingState = null;
			}
			finally
			{
				typingState?.Dispose();
			}
		}
	}
}
