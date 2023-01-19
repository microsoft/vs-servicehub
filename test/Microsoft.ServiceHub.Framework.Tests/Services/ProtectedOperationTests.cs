// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework.Services;
using Xunit;

public class ProtectedOperationTests
{
	private const string OpMk1 = "op1";
	private const string OpMk2 = "op2";

	[Fact]
	public void Equality()
	{
#pragma warning disable CS0618 // Type or member is obsolete
		Assert.Equal(new ProtectedOperation(), new ProtectedOperation());
		Assert.False(new ProtectedOperation().Equals("string"));
#pragma warning restore CS0618 // Type or member is obsolete

		Assert.Equal(new ProtectedOperation(OpMk1), new ProtectedOperation(OpMk1));
		Assert.Equal(new ProtectedOperation(OpMk1, 3), new ProtectedOperation(OpMk1, 3));

		Assert.NotEqual(new ProtectedOperation(OpMk1), new ProtectedOperation(OpMk2));
		Assert.NotEqual(new ProtectedOperation(OpMk1, 2), new ProtectedOperation(OpMk1, 3));
		Assert.NotEqual(new ProtectedOperation(OpMk1), new ProtectedOperation(OpMk1, 3));
	}

	[Fact]
	public void IsSupersetOf()
	{
		var bigOp = new ProtectedOperation(OpMk1, 3);
		var bigOp2 = new ProtectedOperation(OpMk1, 3);
		var smallOp = new ProtectedOperation(OpMk1, 1);
		var unrelatedOp = new ProtectedOperation(OpMk2);

		Assert.True(bigOp.IsSupersetOf(bigOp));
		Assert.True(unrelatedOp.IsSupersetOf(unrelatedOp));

		Assert.True(bigOp.IsSupersetOf(smallOp));
		Assert.False(smallOp.IsSupersetOf(bigOp));

		Assert.True(bigOp.IsSupersetOf(bigOp2));
		Assert.True(bigOp2.IsSupersetOf(bigOp));

		Assert.False(bigOp.IsSupersetOf(unrelatedOp));
		Assert.False(unrelatedOp.IsSupersetOf(bigOp));
	}

	[Fact]
	public void Ctor_RejectsNull()
	{
		Assert.Throws<ArgumentNullException>("operationMoniker", () => new ProtectedOperation(null!));
	}

	[Fact]
	public void MonikerDoesNotReturnNull()
	{
#pragma warning disable CS0618 // Type or member is obsolete
		var operation = new ProtectedOperation();
#pragma warning restore CS0618 // Type or member is obsolete
		Assert.Throws<InvalidOperationException>(() => operation.OperationMoniker);
	}
}
