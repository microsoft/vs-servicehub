// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Analyzers;

public readonly struct Option<T>
{
	private readonly bool hasValue;
	private readonly T value;

	private Option(bool hasValue, T value)
	{
		this.hasValue = hasValue;
		this.value = value;
	}

	public static Option<T> None => new(false, default!);

	public bool HasValue => this.hasValue;

	public T Value => this.hasValue ? this.value : throw new InvalidOperationException("No value present");

	public static implicit operator Option<T>(T value) => Some(value);

	public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

	public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

	public static Option<T> Some(T value) => new(true, value);

	public Option<TResult> Map<TResult>(Func<T, TResult> mapper)
		=> this.hasValue ? Option<TResult>.Some(mapper(this.value)) : Option<TResult>.None;

	public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> binder)
		=> this.hasValue ? binder(this.value) : Option<TResult>.None;

	public Option<T> OrElse(Func<Option<T>> alternative)
		=> this.hasValue ? this : alternative();

	public T OrElse(T defaultValue)
		=> this.hasValue ? this.value : defaultValue;

	public override bool Equals(object? obj)
		=> obj is Option<T> other && this.hasValue == other.hasValue && EqualityComparer<T>.Default.Equals(this.value, other.value);

	public override int GetHashCode()
		=> this.hasValue is false ? 0 : EqualityComparer<T>.Default.GetHashCode(this.value);
}

public static class Option
{
	public static Option<T> Some<T>(T value) => Option<T>.Some(value);

	public static Option<T> None<T>() => Option<T>.None;
}
