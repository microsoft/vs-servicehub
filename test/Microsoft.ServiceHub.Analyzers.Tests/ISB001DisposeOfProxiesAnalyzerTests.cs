// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.ServiceHub.Analyzers;
using Xunit;
using Verify = CSharpCodeFixVerifier<Microsoft.ServiceHub.Analyzers.ISB001DisposeOfProxiesAnalyzer, Microsoft.CodeAnalysis.Testing.EmptyCodeFixProvider>;

public class ISB001DisposeOfProxiesAnalyzerTests
{
	private const string Preamble = @"
using System;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;
interface IFoo { }
static class Stock {
    internal static ServiceJsonRpcDescriptor Descriptor => throw new NotImplementedException();
}
";

	[Fact]
	public async Task GetProxyAsync_IgnoredAwaitedResult()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        await [|sb.GetProxyAsync<IFoo>(Stock.Descriptor)|];
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.NonDisposalDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_IgnoredResult()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        [|sb.GetProxyAsync<IFoo>(Stock.Descriptor)|];
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.NonDisposalDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_DisposedWithoutFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = await [|sb.GetProxyAsync<IFoo>(Stock.Descriptor)|];
        """".ToString(); // something else that might throw
        (client as IDisposable)?.Dispose();
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.NonDisposalDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_InsideTryCatchWithNoFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        try {
            var client = await [|sb.GetProxyAsync<IFoo>(Stock.Descriptor)|];
        } catch {
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.NonDisposalDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_DisposedWithFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        try {
            """".ToString(); // something else that might throw
        }
        finally {
            (client as IDisposable)?.Dispose();
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_DisposedByUsing()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        using (client as IDisposable) {
            """".ToString(); // something else that might throw
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_UnconditionalCastToIDisposable()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        try {
            """".ToString(); // something else that might throw
        }
        finally {
            ((IDisposable)client).Dispose();
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_WithinTry_DisposedWithFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = null;
        try {
            client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
            """".ToString(); // something else that might throw
        }
        finally {
            (client as IDisposable)?.Dispose();
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_WithinDoublyNestedTry_DisposedWithFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = null;
        try {
            try {
                client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
                """".ToString(); // something else that might throw
            } catch {
            }
        }
        finally {
            (client as IDisposable)?.Dispose();
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_DisposedImmediatelyFinally()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        IFoo client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        (client as IDisposable)?.Dispose();
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndDisposedLater()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        (client as IDisposable)?.Dispose();
        client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndDisposedLaterInDisposeBool()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        (client as IDisposable)?.Dispose();
        client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            (client as IDisposable)?.Dispose();
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_InDisposableTypeButNotDisposed()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo [|client|];
    async Task Foo(IServiceBroker sb) {
        (client as IDisposable)?.Dispose();
        client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing) {
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.ProxyMemberMustBeDisposedInDisposeMethodDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndDisposedBeforeButNotLater()
	{
		string test = Preamble + @"
class Test {
    IFoo [|client|];
    async Task Foo(IServiceBroker sb) {
        (client as IDisposable)?.Dispose();
        client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.ProxyMemberMustBeDisposedInDisposeMethodDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndPossiblyOverwrittenWithoutDisposal()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        [|client|] = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.OverwrittenMemberDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndPossiblyOverwrittenWithoutDisposalWithIneffectiveCheck1()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if (client != null) {
            [|client|] = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.OverwrittenMemberDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndPossiblyOverwrittenWithoutDisposalWithIneffectiveCheck2()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if (client is object) {
            [|client|] = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.OverwrittenMemberDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_AndPossiblyOverwrittenWithoutDisposalWithIneffectiveCheck3()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if ("""" == null) {
            [|client|] = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.OverwrittenMemberDescriptor);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_WithIsNullCheckBeforeAssignment()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if (client is null) {
            client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_WithEqualsNullCheckBeforeAssignment()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if (client == null) {
            client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_WithNullEqualsCheckBeforeAssignment()
	{
		string test = Preamble + @"
class Test : IDisposable {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        if (null == client) {
            client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        }
    }

    public void Dispose() => (client as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task GetProxyAsync_StoredInField_ButNeverDisposed()
	{
		string test = Preamble + @"
class Test {
    IFoo client;
    async Task Foo(IServiceBroker sb) {
        client = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
    }
}
";

		await Verify.VerifyAnalyzerAsync(
			test,
			new DiagnosticResult(ISB001DisposeOfProxiesAnalyzer.OverwrittenMemberDescriptor).WithSpan(13, 9, 13, 15).WithArguments("client"),
			new DiagnosticResult(ISB001DisposeOfProxiesAnalyzer.ProxyMemberMustBeDisposedInDisposeMethodDescriptor).WithSpan(11, 10, 11, 16).WithArguments("Test", "client"));
	}

	[Fact]
	public async Task GetProxyAsync_CalledWithinUsingResourcesList()
	{
		string test = Preamble + @"
class Test {
    async Task Foo(IServiceBroker sb) {
        using (var client = await sb.GetProxyAsync<IDisposable>(Stock.Descriptor))
        {
        }
    }
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task ServiceBrokerClient_CtorStoresInFieldAndDisposedProperly()
	{
		string test = Preamble + @"
class Test : IDisposable {
    ServiceBrokerClient sbc;

    public Test(IServiceBroker sb) {
        sbc = new ServiceBrokerClient(sb);
    }

    public void Dispose() => sbc.Dispose();
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task ServiceBrokerClient_CtorStoresInFieldAndDisposedProperlyWithExplicitIDisposableImplementation()
	{
		string test = Preamble + @"
class Test : IDisposable {
    ServiceBrokerClient sbc;

    public Test(IServiceBroker sb) {
        sbc = new ServiceBrokerClient(sb);
    }

    void IDisposable.Dispose() => sbc.Dispose();
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task ServiceBrokerClient_MethodStoresInFieldAndDisposedProperly()
	{
		string test = Preamble + @"
class Test : IDisposable {
    ServiceBrokerClient sbc;

    public void Foo(IServiceBroker sb) {
        sbc?.Dispose();
        sbc = new ServiceBrokerClient(sb);
    }

    public void Dispose() => sbc.Dispose();
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task ServiceBrokerClient_MethodStoresInFieldAndDisposedWithNullCheckProperly()
	{
		string test = Preamble + @"
class Test : IDisposable {
    ServiceBrokerClient sbc;

    public void Foo(IServiceBroker sb) {
        sbc?.Dispose();
        sbc = new ServiceBrokerClient(sb);
    }

    public void Dispose() => sbc?.Dispose();
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact]
	public async Task ServiceBrokerClient_CtorStoresInFieldButDoesNotDispose()
	{
		string test = Preamble + @"
class Test : IDisposable {
    ServiceBrokerClient [|sbc|];

    public Test(IServiceBroker sb) {
        sbc = new ServiceBrokerClient(sb);
    }

    public void Dispose() { }
}";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.ProxyMemberMustBeDisposedInDisposeMethodDescriptor);
	}

	[Fact]
	public async Task ServiceBrokerClient_MethodStoresInLocal_WithoutDisposal()
	{
		string test = Preamble + @"
class Test {
    void Foo(IServiceBroker sb) {
        var sbc = [|new ServiceBrokerClient(sb)|];
    }
}";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.NonDisposalDescriptor);
	}

	[Fact]
	public async Task ServiceBrokerClient_MethodStoresInLocal_AndDisposesWithUsing()
	{
		string test = Preamble + @"
class Test {
    void Foo(IServiceBroker sb) {
        using (var sbc = new ServiceBrokerClient(sb)) {
        }
    }
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact(Skip = "Not supported till our analyzers can target C# 8")]
	public async Task ServiceBrokerClient_MethodStoresInLocal_AndDisposesWithUsingVar()
	{
		string test = Preamble + @"
class Test {
    void Foo(IServiceBroker sb) {
        using var sbc = new ServiceBrokerClient(sb);
    }
}";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact(Skip = "Not yet implemented.")]
	public async Task GetProxyAsync_AsyncFactoryPattern()
	{
		string test = Preamble + @"
class Test : IDisposable {
    readonly IFoo foo;

    private Test(IFoo foo) {
        this.foo = foo;
    }

    static async Task<Test> CreateAsync(IServiceBroker sb) {
        IFoo foo = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        return new Test(foo);
    }

    public void Dispose() => (this.foo as IDisposable)?.Dispose();
}
";

		await Verify.VerifyAnalyzerAsync(test);
	}

	[Fact(Skip = "Not yet implemented.")]
	public async Task GetProxyAsync_AsyncFactoryPattern_NotDisposedOf()
	{
		string test = Preamble + @"
class Test {
    readonly IFoo [|foo|];

    private Test(IFoo foo) {
        this.foo = foo;
    }

    static async Task<Test> CreateAsync(IServiceBroker sb) {
        IFoo foo = await sb.GetProxyAsync<IFoo>(Stock.Descriptor);
        return new Test(foo);
    }
}
";

		await Verify.VerifyAnalyzerAsync(test, ISB001DisposeOfProxiesAnalyzer.ProxyMemberMustBeDisposedInDisposeMethodDescriptor);
	}
}
