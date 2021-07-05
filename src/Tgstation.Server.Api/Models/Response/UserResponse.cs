﻿using System.ComponentModel.DataAnnotations;

using Tgstation.Server.Api.Models.Internal;

namespace Tgstation.Server.Api.Models.Response
{
	/// <inheritdoc />
	public class UserResponse : UserApiBase
	{
		/// <summary>
		/// The <see cref="UserResponse"/> who created this <see cref="UserResponse"/>.
		/// </summary>
		[Required]
		public UserName? CreatedBy { get; set; }
	}
}
