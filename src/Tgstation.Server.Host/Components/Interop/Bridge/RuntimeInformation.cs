﻿using System;
using System.Collections.Generic;
using System.Linq;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components.Interop.Bridge
{
	/// <summary>
	/// Representation of the initial data passed as part of a <see cref="BridgeCommandType.Startup"/> request.
	/// </summary>
	public sealed class RuntimeInformation : ChatUpdate
	{
		/// <summary>
		/// The <see cref="IAssemblyInformationProvider.Version"/>.
		/// </summary>
		public Version ServerVersion { get; }

		/// <summary>
		/// The port the HTTP server is running on
		/// </summary>
		public ushort ServerPort { get; }

		/// <summary>
		/// If DD should just respond if it's API is working and then exit.
		/// </summary>
		public bool ApiValidateOnly { get; }

		/// <summary>
		/// The <see cref="Api.Models.Instance.Name"/> of the owner at the time of launch
		/// </summary>
		public string InstanceName { get; }

		/// <summary>
		/// The <see cref="Api.Models.Internal.RevisionInformation"/> of the launch
		/// </summary>
		public Api.Models.Internal.RevisionInformation Revision { get; }

		/// <summary>
		/// The <see cref="DreamDaemonSecurity"/> level of the launch
		/// </summary>
		public DreamDaemonSecurity? SecurityLevel { get; }

		/// <summary>
		/// The <see cref="TestMergeInformation"/>s in the launch.
		/// </summary>
		public IReadOnlyCollection<TestMergeInformation> TestMerges { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="RuntimeInformation"/> <see langword="class"/>.
		/// </summary>
		/// <param name="assemblyInformationProvider">The <see cref="IAssemblyInformationProvider"/> to use.</param>
		/// <param name="serverPortProvider">The <see cref="IServerPortProvider"/> used to set the value of <see cref="ServerPort"/>.</param>
		/// <param name="testMerges">An <see cref="IEnumerable{T}"/> used to construct the value of <see cref="TestMerges"/>.</param>
		/// <param name="chatChannels">The <see cref="Chat.ChannelRepresentation"/>s for the <see cref="ChatUpdate"/>.</param>
		/// <param name="instance">The <see cref="Instance"/> used to set <see cref="InstanceName"/>.</param>
		/// <param name="revision">The value of <see cref="RevisionInformation"/>.</param>
		/// <param name="securityLevel">The value of <see cref="SecurityLevel"/>.</param>
		/// <param name="apiValidateOnly">The value of <see cref="ApiValidateOnly"/>.</param>
		public RuntimeInformation(
			IAssemblyInformationProvider assemblyInformationProvider,
			IServerPortProvider serverPortProvider,
			IEnumerable<TestMergeInformation> testMerges,
			IEnumerable<Chat.ChannelRepresentation> chatChannels,
			Api.Models.Instance instance,
			Api.Models.Internal.RevisionInformation revision,
			DreamDaemonSecurity? securityLevel,
			bool apiValidateOnly)
			: base(chatChannels)
		{
			ServerVersion = assemblyInformationProvider?.Version ?? throw new ArgumentNullException(nameof(assemblyInformationProvider));
			ServerPort = serverPortProvider?.HttpApiPort ?? throw new ArgumentNullException(nameof(serverPortProvider));
			TestMerges = testMerges?.ToList() ?? throw new ArgumentNullException(nameof(testMerges));
			InstanceName = instance?.Name ?? throw new ArgumentNullException(nameof(instance));
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			SecurityLevel = securityLevel;
			ApiValidateOnly = apiValidateOnly;
		}
	}
}
