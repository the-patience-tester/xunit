using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

public static class TestCollectionRunnerTests
{
	public class Messages
	{
		[Fact]
		public async ValueTask OnTestCollectionCleanupFailure()
		{
			var runner = new TestableTestCollectionRunner();
			var ex = Record.Exception(ThrowException);

			await runner.OnTestCollectionCleanupFailure(ex!);

			var message = Assert.Single(runner.MessageBus.Messages);
			var failure = Assert.IsAssignableFrom<ITestCollectionCleanupFailure>(message);

			VerifyTestCollectionMessage(failure);
			Assert.Equal(-1, failure.ExceptionParentIndices.Single());
			Assert.Equal(typeof(DivideByZeroException).FullName, failure.ExceptionTypes.Single());
			Assert.Equal("Attempted to divide by zero.", failure.Messages.Single());
			Assert.NotEmpty(failure.StackTraces.Single()!);
		}

		[Fact]
		public async ValueTask OnTestCollectionFinished()
		{
			var runner = new TestableTestCollectionRunner();
			var summary = new RunSummary { Total = 2112, Failed = 42, Skipped = 21, NotRun = 9, Time = 123.45m };

			await runner.OnTestCollectionFinished(summary);

			var message = Assert.Single(runner.MessageBus.Messages);
			var finished = Assert.IsAssignableFrom<ITestCollectionFinished>(message);

			VerifyTestCollectionMessage(finished);
			Assert.Equal(123.45m, finished.ExecutionTime);
			Assert.Equal(42, finished.TestsFailed);
			Assert.Equal(9, finished.TestsNotRun);
			Assert.Equal(21, finished.TestsSkipped);
			Assert.Equal(2112, finished.TestsTotal);
		}

		[Fact]
		public async ValueTask OnTestCollectionStarting()
		{
			var runner = new TestableTestCollectionRunner();

			await runner.OnTestCollectionStarting();

			var message = Assert.Single(runner.MessageBus.Messages);
			var starting = Assert.IsAssignableFrom<ITestCollectionStarting>(message);

			VerifyTestCollectionMessage(starting);
			Assert.Null(starting.TestCollectionClassName);
			Assert.Equal("test-collection-display-name", starting.TestCollectionDisplayName);
			Assert.Equivalent(TestData.DefaultTraits, starting.Traits);
		}

		static void ThrowException() =>
			throw new DivideByZeroException();

		static void VerifyTestCollectionMessage(ITestCollectionMessage message)
		{
			Assert.Equal("assembly-id", message.AssemblyUniqueID);
			Assert.Equal("test-collection-id", message.TestCollectionUniqueID);
		}
	}

	public class Cancellation
	{
		[Fact]
		public static async ValueTask OnTestCollectionStarting()
		{
			var summary = new RunSummary { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12m };
			var runner = new TestableTestCollectionRunner
			{
				OnTestCollectionStarting__Result = false,
				RunTestClassAsync__Result = summary,
			};

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				// RunTestClassAsync
				"OnTestCollectionFinished(summary: { Total = 0 })",
				// OnTestCollectionCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestCollectionFinished()
		{
			var runner = new TestableTestCollectionRunner { OnTestCollectionFinished__Result = false };

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"RunTestClassAsync(testClass: 'test-class-name', testCases: ['test-case-display-name'])",
				"OnTestCollectionFinished(summary: { Total = 0 })",
				// OnTestCollectionCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestCollectionCleanupFailure()
		{
			// Need to throw in OnTestCollectionFinished to get OnTestCollectionCleanupFailure to trigger
			var runner = new TestableTestCollectionRunner
			{
				OnTestCollectionCleanupFailure__Result = false,
				OnTestCollectionFinished__Lambda = () => throw new DivideByZeroException(),
			};

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"RunTestClassAsync(testClass: 'test-class-name', testCases: ['test-case-display-name'])",
				"OnTestCollectionFinished(summary: { Total = 0 })",
				"OnTestCollectionCleanupFailure(exception: typeof(DivideByZeroException))",
			}, runner.Invocations);
		}
	}

	public class ExceptionHandling
	{
		[Fact]
		public static async ValueTask NoExceptions()
		{
			var summary = new RunSummary { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12m };
			var runner = new TestableTestCollectionRunner { RunTestClassAsync__Result = summary };

			var result = await runner.RunAsync();

			Assert.Equal(summary, result);
			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"RunTestClassAsync(testClass: 'test-class-name', testCases: ['test-case-display-name'])",
				"OnTestCollectionFinished(summary: { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12 })",
				// OnTestCollectionCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestCollectionStarting()
		{
			var runner = new TestableTestCollectionRunner { OnTestCollectionStarting__Lambda = () => throw new DivideByZeroException() };

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"FailTestClass(testClass: 'test-class-name', testCases: ['test-case-display-name'], exception: typeof(DivideByZeroException))",
				"OnTestCollectionFinished(summary: { Total = 0 })",
				// OnTestCollectionCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestCollectionFinished()
		{
			var runner = new TestableTestCollectionRunner { OnTestCollectionFinished__Lambda = () => throw new DivideByZeroException() };

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"RunTestClassAsync(testClass: 'test-class-name', testCases: ['test-case-display-name'])",
				"OnTestCollectionFinished(summary: { Total = 0 })",
				"OnTestCollectionCleanupFailure(exception: typeof(DivideByZeroException))",
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestCollectionCleanupFailure()
		{
			// Need to throw in OnTestCollectionFinished to get OnTestCollectionCleanupFailure to trigger
			var runner = new TestableTestCollectionRunner
			{
				OnTestCollectionCleanupFailure__Lambda = () => throw new DivideByZeroException(),
				OnTestCollectionFinished__Lambda = () => throw new ArgumentException(),
			};

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestCollectionStarting",
				"RunTestClassAsync(testClass: 'test-class-name', testCases: ['test-case-display-name'])",
				"OnTestCollectionFinished(summary: { Total = 0 })",
				"OnTestCollectionCleanupFailure(exception: typeof(ArgumentException))",
			}, runner.Invocations);
			Assert.Collection(
				runner.MessageBus.Messages,
				message => Assert.IsAssignableFrom<ITestCollectionStarting>(message),
				message =>
				{
					var errorMessage = Assert.IsAssignableFrom<IErrorMessage>(message);
					Assert.Equal(new[] { -1 }, errorMessage.ExceptionParentIndices);
					Assert.Equal(new[] { "System.DivideByZeroException" }, errorMessage.ExceptionTypes);
					Assert.Equal(new[] { "Attempted to divide by zero." }, errorMessage.Messages);
					Assert.NotEmpty(errorMessage.StackTraces.Single()!);
				}
			);
		}
	}

	class TestableTestCollectionRunner(
		IReadOnlyCollection<ITestCase>? testCases = null,
		ITestCollection? testCollection = null) :
			TestCollectionRunner<TestCollectionRunnerContext<ITestCollection, ITestCase>, ITestCollection, ITestClass, ITestCase>
	{
		readonly IReadOnlyCollection<ITestCase> testCases = testCases ?? [Mocks.TestCase()];
		readonly ITestCollection testCollection = testCollection ?? Mocks.TestCollection();

		public readonly ExceptionAggregator Aggregator = new();
		public readonly CancellationTokenSource CancellationTokenSource = new();
		public readonly List<string> Invocations = [];
		public readonly SpyMessageBus MessageBus = new();

		public RunSummary FailTestClass__Result = new();

		protected override ValueTask<RunSummary> FailTestClass(
			TestCollectionRunnerContext<ITestCollection, ITestCase> ctxt,
			ITestClass? testClass,
			IReadOnlyCollection<ITestCase> testCases,
			Exception? exception)
		{
			Invocations.Add($"FailTestClass(testClass: '{testClass?.TestClassName ?? "(null)"}', testCases: [{string.Join(",", testCases.Select(tc => "'" + tc.TestCaseDisplayName + "'"))}], exception: {TypeName(exception)})");

			return new(FailTestClass__Result);
		}

		public Action? OnTestCollectionCleanupFailure__Lambda = null;
		public bool OnTestCollectionCleanupFailure__Result = true;

		public async ValueTask<bool> OnTestCollectionCleanupFailure(Exception exception)
		{
			await using var ctxt = new TestCollectionRunnerContext<ITestCollection, ITestCase>(testCollection, testCases, ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestCollectionCleanupFailure(ctxt, exception);
		}

		protected override async ValueTask<bool> OnTestCollectionCleanupFailure(
			TestCollectionRunnerContext<ITestCollection, ITestCase> ctxt,
			Exception exception)
		{
			Invocations.Add($"OnTestCollectionCleanupFailure(exception: typeof({ArgumentFormatter.FormatTypeName(exception.GetType())}))");

			OnTestCollectionCleanupFailure__Lambda?.Invoke();

			await base.OnTestCollectionCleanupFailure(ctxt, exception);

			return OnTestCollectionCleanupFailure__Result;
		}

		public async ValueTask<bool> OnTestCollectionFinished(RunSummary summary)
		{
			await using var ctxt = new TestCollectionRunnerContext<ITestCollection, ITestCase>(testCollection, testCases, ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestCollectionFinished(ctxt, summary);
		}

		public Action? OnTestCollectionFinished__Lambda = null;
		public bool OnTestCollectionFinished__Result = true;

		protected override async ValueTask<bool> OnTestCollectionFinished(
			TestCollectionRunnerContext<ITestCollection, ITestCase> ctxt,
			RunSummary summary)
		{
			Invocations.Add($"OnTestCollectionFinished(summary: {ArgumentFormatter.Format(summary)})");

			OnTestCollectionFinished__Lambda?.Invoke();

			await base.OnTestCollectionFinished(ctxt, summary);

			return OnTestCollectionFinished__Result;
		}

		public async ValueTask<bool> OnTestCollectionStarting()
		{
			await using var ctxt = new TestCollectionRunnerContext<ITestCollection, ITestCase>(testCollection, testCases, ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestCollectionStarting(ctxt);
		}

		public Action? OnTestCollectionStarting__Lambda = null;
		public bool OnTestCollectionStarting__Result = true;

		protected override async ValueTask<bool> OnTestCollectionStarting(TestCollectionRunnerContext<ITestCollection, ITestCase> ctxt)
		{
			Invocations.Add("OnTestCollectionStarting");

			OnTestCollectionStarting__Lambda?.Invoke();

			await base.OnTestCollectionStarting(ctxt);

			return OnTestCollectionStarting__Result;
		}

		public RunSummary RunTestClassAsync__Result = new();

		protected override ValueTask<RunSummary> RunTestClass(
			TestCollectionRunnerContext<ITestCollection, ITestCase> ctxt,
			ITestClass? testClass,
			IReadOnlyCollection<ITestCase> testCases)
		{
			Invocations.Add($"RunTestClassAsync(testClass: '{testClass?.TestClassName ?? "(null)"}', testCases: [{string.Join(",", testCases.Select(tc => "'" + tc.TestCaseDisplayName + "'"))}])");

			return new(RunTestClassAsync__Result);
		}

		public async ValueTask<RunSummary> RunAsync()
		{
			await using var ctxt = new TestCollectionRunnerContext<ITestCollection, ITestCase>(testCollection, testCases, ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await Run(ctxt);
		}

		static string TypeName(object? obj) =>
			obj is null ? "null" : $"typeof({ArgumentFormatter.FormatTypeName(obj.GetType())})";
	}
}
