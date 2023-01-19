// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;
using Xunit;

public class RemoteServiceConnectionInfoTests
{
	[Fact]
	public void IsEmpty()
	{
		Assert.True(default(RemoteServiceConnectionInfo).IsEmpty);
	}

	[Fact]
	public void IsEmpty_ClrActivation()
	{
		var info = new RemoteServiceConnectionInfo
		{
			ClrActivation = new RemoteServiceConnectionInfo.LocalCLRServiceActivation(@"c:\some path", "some type"),
		};
		Assert.False(info.IsEmpty);
	}

	[Fact]
	public void IsEmpty_PipeName()
	{
		var info = new RemoteServiceConnectionInfo { PipeName = string.Empty };
		Assert.True(info.IsEmpty);

		info = new RemoteServiceConnectionInfo { PipeName = "  " };
		Assert.True(info.IsEmpty);

		info = new RemoteServiceConnectionInfo { PipeName = "some pipe" };
		Assert.False(info.IsEmpty);
	}

	[Fact]
	public void IsEmpty_MultiplexingChannelId()
	{
		var info = new RemoteServiceConnectionInfo { MultiplexingChannelId = 3 };
		Assert.False(info.IsEmpty);
	}

	[Fact]
	public void IsEmpty_RequestId()
	{
		var info = new RemoteServiceConnectionInfo { RequestId = Guid.NewGuid() };
		Assert.False(info.IsEmpty);
	}

	[Fact]
	public void IsOneOf_Empty()
	{
		var info = default(RemoteServiceConnectionInfo);
		Assert.False(info.IsOneOf(RemoteServiceConnections.ClrActivation | RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing));
		Assert.False(info.IsOneOf(RemoteServiceConnections.IpcPipe));
		Assert.False(info.IsOneOf(RemoteServiceConnections.None));
	}

	[Fact]
	public void IsOneOf_Pipe()
	{
		var info = new RemoteServiceConnectionInfo
		{
			PipeName = "some pipe",
		};
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe));
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing));
		Assert.False(info.IsOneOf(RemoteServiceConnections.Multiplexing));
		Assert.False(info.IsOneOf(RemoteServiceConnections.Multiplexing | RemoteServiceConnections.ClrActivation));
		Assert.False(info.IsOneOf(RemoteServiceConnections.None));
	}

	[Fact]
	public void IsOneOf_Multiplexing()
	{
		var info = new RemoteServiceConnectionInfo
		{
			MultiplexingChannelId = 5,
		};
		Assert.False(info.IsOneOf(RemoteServiceConnections.IpcPipe));
		Assert.False(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.ClrActivation));
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing));
		Assert.True(info.IsOneOf(RemoteServiceConnections.Multiplexing));
		Assert.False(info.IsOneOf(RemoteServiceConnections.None));
	}

	[Fact]
	public void IsOneOf_ClrActivation()
	{
		var info = new RemoteServiceConnectionInfo
		{
			ClrActivation = new RemoteServiceConnectionInfo.LocalCLRServiceActivation("a.dll", "a"),
		};
		Assert.False(info.IsOneOf(RemoteServiceConnections.IpcPipe));
		Assert.False(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing));
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing | RemoteServiceConnections.ClrActivation));
		Assert.True(info.IsOneOf(RemoteServiceConnections.ClrActivation));
		Assert.False(info.IsOneOf(RemoteServiceConnections.None));
	}

	[Fact]
	public void IsOneOf_Multiple()
	{
		var info = new RemoteServiceConnectionInfo
		{
			MultiplexingChannelId = 5,
			PipeName = "some pipe",
			ClrActivation = new RemoteServiceConnectionInfo.LocalCLRServiceActivation("a.dll", "a"),
		};
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe));
		Assert.True(info.IsOneOf(RemoteServiceConnections.ClrActivation));
		Assert.True(info.IsOneOf(RemoteServiceConnections.Multiplexing));
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.ClrActivation));
		Assert.True(info.IsOneOf(RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing));
		Assert.False(info.IsOneOf(RemoteServiceConnections.None));
	}
}
