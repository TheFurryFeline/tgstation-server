﻿using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Tgstation.Server.Api.Models.Request;
using Tgstation.Server.Api.Models.Response;

namespace Tgstation.Server.Client.Components
{
	/// <summary>
	/// For managing the <see cref="ByondInstallResponse"/> installation.
	/// </summary>
	public interface IByondClient
	{
		/// <summary>
		/// Get the <see cref="ByondInstallResponse"/> active <see cref="System.Version"/> information.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="ByondInstallResponse"/> active <see cref="System.Version"/> information.</returns>
		Task<ByondResponse> ActiveVersion(CancellationToken cancellationToken);

		/// <summary>
		/// Get all installed <see cref="ByondInstallResponse"/> <see cref="System.Version"/>s.
		/// </summary>
		/// <param name="paginationSettings">The optional <see cref="PaginationSettings"/> for the operation.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in an <see cref="IReadOnlyList{T}"/> of installed <see cref="ByondInstallResponse"/> <see cref="System.Version"/>s.</returns>
		Task<IReadOnlyList<ByondResponse>> InstalledVersions(PaginationSettings? paginationSettings, CancellationToken cancellationToken);

		/// <summary>
		/// Updates the <see cref="ByondInstallResponse"/> information.
		/// </summary>
		/// <param name="installRequest">The <see cref="ByondVersionRequest"/>.</param>
		/// <param name="zipFileStream">The <see cref="Stream"/> for the .zip file if <see cref="ByondVersionRequest.UploadCustomZip"/> is <see langword="true"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the updated <see cref="ByondInstallResponse"/> information.</returns>
		Task<ByondInstallResponse> SetActiveVersion(ByondVersionRequest installRequest, Stream zipFileStream, CancellationToken cancellationToken);
	}
}
