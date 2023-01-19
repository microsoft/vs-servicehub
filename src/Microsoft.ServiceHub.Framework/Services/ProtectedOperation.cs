// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.ServiceHub.Framework.Services;

/// <summary>
/// Describes an operation that requires an authorization check.
/// </summary>
/// <seealso cref="IAuthorizationService.CheckAuthorizationAsync(ProtectedOperation, System.Threading.CancellationToken)"/>
public class ProtectedOperation : IEquatable<ProtectedOperation>
{
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private string? operationMoniker;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtectedOperation"/> class.
	/// </summary>
	[Obsolete("Use a constructor that initializes the properties instead.")]
	public ProtectedOperation()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtectedOperation"/> class.
	/// </summary>
	/// <param name="operationMoniker">the kind of operation to be performed.</param>
	public ProtectedOperation(string operationMoniker)
	{
		Requires.NotNull(operationMoniker, nameof(operationMoniker));

		this.OperationMoniker = operationMoniker;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtectedOperation"/> class.
	/// </summary>
	/// <param name="operationMoniker">the kind of operation to be performed.</param>
	/// <param name="requiredTrustLevel">the trust level required for the operation.</param>
	public ProtectedOperation(string operationMoniker, int requiredTrustLevel)
	{
		Requires.NotNull(operationMoniker, nameof(operationMoniker));

		this.OperationMoniker = operationMoniker;
		this.RequiredTrustLevel = requiredTrustLevel;
	}

	/// <summary>
	/// Gets or sets the kind of operation to be performed.
	/// </summary>
	/// <remarks>
	/// This may be a semi-human readable string, and is NOT intended for machine interpretation.
	/// Processors of this value should consider it an opaque string.
	/// </remarks>
	public string OperationMoniker
	{
		get => this.operationMoniker ?? throw new InvalidOperationException(Strings.NotInitialized);
		set
		{
			// Allow setting null until we've been assigned a non-null value.
			if (this.operationMoniker is object)
			{
				Requires.NotNull(value, nameof(value));
			}

			this.operationMoniker = value;
		}
	}

	/// <summary>
	/// Gets or sets the trust level required for the operation.
	/// </summary>
	/// <value>May be null if not applicable (e.g. the <see cref="OperationMoniker"/> of operation is simply allowed or not, without multiple degrees of trust).</value>
	public int? RequiredTrustLevel { get; set; }

	/// <inheritdoc />
	public override bool Equals(object? obj) => this.Equals(obj as ProtectedOperation);

	/// <inheritdoc />
	public override int GetHashCode() => this.OperationMoniker?.GetHashCode() ?? 0 + this.RequiredTrustLevel.GetHashCode();

	/// <inheritdoc />
	public virtual bool Equals(ProtectedOperation? other)
	{
		if (other == null)
		{
			return false;
		}

		return this.operationMoniker == other.operationMoniker
			&& this.RequiredTrustLevel == other.RequiredTrustLevel;
	}

	/// <summary>
	/// Gets a value indicating whether this <see cref="ProtectedOperation"/>, if granted, implies another <see cref="ProtectedOperation"/> should also be considered granted.
	/// </summary>
	/// <param name="other">The other operation, which may be a subset of this one.</param>
	/// <returns><see langword="true"/> if this instance is a superset of the other; <see langword="false"/> otherwise.</returns>
	/// <remarks>
	/// In the base implementation, a superset is considered true if the <see cref="OperationMoniker"/> is equal and <see cref="RequiredTrustLevel"/> is equal or a greater value.
	/// </remarks>
	public virtual bool IsSupersetOf(ProtectedOperation other)
	{
		Requires.NotNull(other, nameof(other));

		return this.OperationMoniker == other.OperationMoniker
			&& (this.RequiredTrustLevel == other.RequiredTrustLevel || (this.RequiredTrustLevel.HasValue && other.RequiredTrustLevel.HasValue && this.RequiredTrustLevel.Value > other.RequiredTrustLevel.Value));
	}
}
