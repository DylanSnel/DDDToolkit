using DTS.DDD.Results;

namespace DTS.DDD.Mediator;

/// <summary>A command that answers a <see cref="Result{T}"/>: short for <c>ICommand&lt;Result&lt;TValue&gt;&gt;</c>.</summary>
/// <typeparam name="TValue">The value of a success.</typeparam>
public interface IResultCommand<TValue> : ICommand<Result<TValue>>;

/// <summary>A command that answers a <see cref="Result"/> without a value: short for <c>ICommand&lt;Result&gt;</c>.</summary>
public interface IResultCommand : ICommand<Result>;

/// <summary>A query that answers a <see cref="Result{T}"/>: short for <c>IQuery&lt;Result&lt;TValue&gt;&gt;</c>.</summary>
/// <typeparam name="TValue">The value of a success.</typeparam>
public interface IResultQuery<TValue> : IQuery<Result<TValue>>;

/// <summary>
/// The handler of an <see cref="IResultCommand{TValue}"/>: short for
/// <c>ICommandHandler&lt;TCommand, Result&lt;TValue&gt;&gt;</c>. <c>Handle</c> returns <c>ValueTask&lt;Result&lt;TValue&gt;&gt;</c>.
/// </summary>
public interface IResultCommandHandler<in TCommand, TValue> : ICommandHandler<TCommand, Result<TValue>>
    where TCommand : ICommand<Result<TValue>>;

/// <summary>The handler of an <see cref="IResultCommand"/>: short for <c>ICommandHandler&lt;TCommand, Result&gt;</c>.</summary>
public interface IResultCommandHandler<in TCommand> : ICommandHandler<TCommand, Result>
    where TCommand : ICommand<Result>;

/// <summary>
/// The handler of an <see cref="IResultQuery{TValue}"/>: short for
/// <c>IQueryHandler&lt;TQuery, Result&lt;TValue&gt;&gt;</c>. <c>Handle</c> returns <c>ValueTask&lt;Result&lt;TValue&gt;&gt;</c>.
/// </summary>
public interface IResultQueryHandler<in TQuery, TValue> : IQueryHandler<TQuery, Result<TValue>>
    where TQuery : IQuery<Result<TValue>>;