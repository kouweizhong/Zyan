﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security;
using System.Runtime.Serialization;

namespace Zyan.Communication.Security.Exceptions
{
	/// <summary>
	/// Security exception which is thrown if user account is locked.
	/// </summary>
	[Serializable]
	public class AccountLockedException : SecurityException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLockedException"/> class.
		/// </summary>
		public AccountLockedException()
			: base(LanguageResource.AccountLockedException_DefaultMessage)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLockedException"/> class.
		/// </summary>
		/// <param name="message">Exception message.</param>
		public AccountLockedException(string message)
			: base(message)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLockedException"/> class.
		/// </summary>
		/// <param name="message">Exception message.</param>
		/// <param name="inner">The inner <see cref="Exception"/>.</param>
		public AccountLockedException(string message, Exception inner)
			: base(message, inner)
		{ 
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountLockedException"/> class.
		/// </summary>
		/// <param name="info">The object that holds the serialized object data.</param>
		/// <param name="context">The contextual information about the source or destination.</param>
		protected AccountLockedException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{ 
		}
	}
}