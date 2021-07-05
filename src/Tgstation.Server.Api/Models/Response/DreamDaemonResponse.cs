﻿using System.ComponentModel.DataAnnotations;

using Tgstation.Server.Api.Models.Internal;

namespace Tgstation.Server.Api.Models.Response
{
	/// <summary>
	/// Represents an instance of BYOND's DreamDaemon game server. Create action starts the server. Delete action shuts down the server.
	/// </summary>
	public sealed class DreamDaemonResponse : DreamDaemonApiBase
	{
		/// <summary>
		/// The live revision.
		/// </summary>
		[ResponseOptions]
		public CompileJobResponse? ActiveCompileJob { get; set; }

		/// <summary>
		/// The next revision to go live.
		/// </summary>
		[ResponseOptions]
		public CompileJobResponse? StagedCompileJob { get; set; }

		/// <summary>
		/// The current <see cref="WatchdogStatus"/>.
		/// </summary>
		[EnumDataType(typeof(WatchdogStatus))]
		[ResponseOptions]
		public WatchdogStatus? Status { get; set; }

		/// <summary>
		/// The current <see cref="DreamDaemonSecurity"/> of <see cref="DreamDaemonResponse"/>. May be downgraded due to requirements of <see cref="ActiveCompileJob"/>.
		/// </summary>
		[EnumDataType(typeof(DreamDaemonSecurity))]
		[ResponseOptions]
		public DreamDaemonSecurity? CurrentSecurity { get; set; }

		/// <summary>
		/// The port the running <see cref="DreamDaemonResponse"/> instance is set to.
		/// </summary>
		[ResponseOptions]
		public ushort? CurrentPort { get; set; }

		/// <summary>
		/// The webclient status the running <see cref="DreamDaemonResponse"/> instance is set to.
		/// </summary>
		[ResponseOptions]
		public bool? CurrentAllowWebclient { get; set; }
	}
}
