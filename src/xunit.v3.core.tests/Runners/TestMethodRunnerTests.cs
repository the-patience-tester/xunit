using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

public class TestMethodRunnerTests
{
	public class Messages
	{
		[Fact]
		public async ValueTask OnTestMethodCleanupFailure()
		{
			var runner = new TestableTestMethodRunner();
			var ex = Record.Exception(ThrowException);

			await runner.OnTestMethodCleanupFailure(ex!);

			var message = Assert.Single(runner.MessageBus.Messages);
			var failure = Assert.IsAssignableFrom<ITestMethodCleanupFailure>(message);

			VerifyTestMethodMessage(failure);
			Assert.Equal(-1, failure.ExceptionParentIndices.Single());
			Assert.Equal(typeof(DivideByZeroException).FullName, failure.ExceptionTypes.Single());
			Assert.Equal("Attempted to divide by zero.", failure.Messages.Single());
			Assert.NotEmpty(failure.StackTraces.Single()!);
		}

		[Fact]
		public async ValueTask OnTestMethodFinished()
		{
			var runner = new TestableTestMethodRunner();
			var summary = new RunSummary { Total = 2112, Failed = 42, Skipped = 21, NotRun = 9, Time = 123.45m };

			await runner.OnTestMethodFinished(summary);

			var message = Assert.Single(runner.MessageBus.Messages);
			var finished = Assert.IsAssignableFrom<ITestMethodFinished>(message);

			VerifyTestMethodMessage(finished);
			Assert.Equal(123.45m, finished.ExecutionTime);
			Assert.Equal(42, finished.TestsFailed);
			Assert.Equal(9, finished.TestsNotRun);
			Assert.Equal(21, finished.TestsSkipped);
			Assert.Equal(2112, finished.TestsTotal);
		}

		[Fact]
		public async ValueTask OnTestMethodStarting()
		{
			var runner = new TestableTestMethodRunner();

			await runner.OnTestMethodStarting();

			var message = Assert.Single(runner.MessageBus.Messages);
			var starting = Assert.IsAssignableFrom<ITestMethodStarting>(message);

			VerifyTestMethodMessage(starting);
			Assert.Equal("test-method", starting.MethodName);
			Assert.Equivalent(TestData.DefaultTraits, starting.Traits);
		}

		static void ThrowException() =>
			throw new DivideByZeroException();

		static void VerifyTestMethodMessage(ITestMethodMessage message)
		{
			Assert.Equal("assembly-id", message.AssemblyUniqueID);
			Assert.Equal("test-collection-id", message.TestCollectionUniqueID);
			Assert.Equal("test-class-id", message.TestClassUniqueID);
			Assert.Equal("test-method-id", message.TestMethodUniqueID);
		}
	}

	public class Cancellation
	{
		[Fact]
		public static async ValueTask OnTestMethodStarting()
		{
			var summary = new RunSummary { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12m };
			var runner = new TestableTestMethodRunner
			{
				OnTestMethodStarting__Result = false,
				RunTestCaseAsync__Result = summary,
			};

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				// RunTestCaseAsync
				"OnTestMethodFinished(summary: { Total = 0 })",
				// OnTestMethodCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestMethodFinished()
		{
			var runner = new TestableTestMethodRunner { OnTestMethodFinished__Result = false };

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"RunTestCaseAsync(testCase: \"test-case-display-name\")",
				"OnTestMethodFinished(summary: { Total = 0 })",
				// OnTestMethodCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestMethodCleanupFailure()
		{
			// Need to throw in OnTestMethodFinished to get OnTestMethodCleanupFailure to trigger
			var runner = new TestableTestMethodRunner
			{
				OnTestMethodCleanupFailure__Result = false,
				OnTestMethodFinished__Lambda = () => throw new DivideByZeroException(),
			};

			await runner.RunAsync();

			Assert.True(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"RunTestCaseAsync(testCase: \"test-case-display-name\")",
				"OnTestMethodFinished(summary: { Total = 0 })",
				"OnTestMethodCleanupFailure(exception: typeof(DivideByZeroException))",
			}, runner.Invocations);
		}
	}

	public class ExceptionHandling
	{
		[Fact]
		public static async ValueTask NoExceptions()
		{
			var summary = new RunSummary { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12m };
			var runner = new TestableTestMethodRunner { RunTestCaseAsync__Result = summary };

			var result = await runner.RunAsync();

			Assert.Equal(summary, result);
			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"RunTestCaseAsync(testCase: \"test-case-display-name\")",
				"OnTestMethodFinished(summary: { Total = 9, Failed = 2, Skipped = 1, NotRun = 3, Time = 21.12 })",
				// OnTestMethodCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestMethodStarting()
		{
			var runner = new TestableTestMethodRunner { OnTestMethodStarting__Lambda = () => throw new DivideByZeroException() };

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"FailTestCase(testCase: \"test-case-display-name\", exception: typeof(DivideByZeroException))",
				"OnTestMethodFinished(summary: { Total = 0 })",
				// OnTestMethodCleanupFailure
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestMethodFinished()
		{
			var runner = new TestableTestMethodRunner { OnTestMethodFinished__Lambda = () => throw new DivideByZeroException() };

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"RunTestCaseAsync(testCase: \"test-case-display-name\")",
				"OnTestMethodFinished(summary: { Total = 0 })",
				"OnTestMethodCleanupFailure(exception: typeof(DivideByZeroException))",
			}, runner.Invocations);
		}

		[Fact]
		public static async ValueTask OnTestMethodCleanupFailure()
		{
			// Need to throw in OnTestMethodFinished to get OnTestMethodCleanupFailure to trigger
			var runner = new TestableTestMethodRunner
			{
				OnTestMethodCleanupFailure__Lambda = () => throw new DivideByZeroException(),
				OnTestMethodFinished__Lambda = () => throw new ArgumentException(),
			};

			await runner.RunAsync();

			Assert.False(runner.CancellationTokenSource.IsCancellationRequested);
			Assert.Equal(new[]
			{
				"OnTestMethodStarting",
				"RunTestCaseAsync(testCase: \"test-case-display-name\")",
				"OnTestMethodFinished(summary: { Total = 0 })",
				"OnTestMethodCleanupFailure(exception: typeof(ArgumentException))",
			}, runner.Invocations);
			Assert.Collection(
				runner.MessageBus.Messages,
				message => Assert.IsAssignableFrom<ITestMethodStarting>(message),
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

	class TestableTestMethodRunner(ITestCase? testCase = null) :
		TestMethodRunner<TestMethodRunnerContext<ITestMethod, ITestCase>, ITestMethod, ITestCase>
	{
		readonly ITestCase testCase = testCase ?? Mocks.TestCase();

		public readonly ExceptionAggregator Aggregator = new();
		public readonly CancellationTokenSource CancellationTokenSource = new();
		public readonly List<string> Invocations = [];
		public readonly SpyMessageBus MessageBus = new();
		ITestMethod TestMethod => Guard.ArgumentNotNull(testCase.TestMethod);

		public RunSummary FailTestCase__Result = new();

		protected override ValueTask<RunSummary> FailTestCase(
			TestMethodRunnerContext<ITestMethod, ITestCase> ctxt,
			ITestCase testCase,
			Exception? exception)
		{
			Invocations.Add($"FailTestCase(testCase: \"{testCase.TestCaseDisplayName}\", exception: {TypeName(exception)})");

			return new(FailTestCase__Result);
		}

		public async ValueTask<bool> OnTestMethodCleanupFailure(Exception exception)
		{
			await using var ctxt = new TestMethodRunnerContext<ITestMethod, ITestCase>(TestMethod, [testCase], ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestMethodCleanupFailure(ctxt, exception);
		}

		public Action? OnTestMethodCleanupFailure__Lambda = null;
		public bool OnTestMethodCleanupFailure__Result = true;

		protected override async ValueTask<bool> OnTestMethodCleanupFailure(
			TestMethodRunnerContext<ITestMethod, ITestCase> ctxt,
			Exception exception)
		{
			Invocations.Add($"OnTestMethodCleanupFailure(exception: typeof({ArgumentFormatter.FormatTypeName(exception.GetType())}))");

			OnTestMethodCleanupFailure__Lambda?.Invoke();

			await base.OnTestMethodCleanupFailure(ctxt, exception);

			return OnTestMethodCleanupFailure__Result;
		}

		public async ValueTask<bool> OnTestMethodFinished(RunSummary summary)
		{
			await using var ctxt = new TestMethodRunnerContext<ITestMethod, ITestCase>(TestMethod, [testCase], ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestMethodFinished(ctxt, summary);
		}

		public Action? OnTestMethodFinished__Lambda = null;
		public bool OnTestMethodFinished__Result = true;

		protected override async ValueTask<bool> OnTestMethodFinished(
			TestMethodRunnerContext<ITestMethod, ITestCase> ctxt,
			RunSummary summary)
		{
			Invocations.Add($"OnTestMethodFinished(summary: {ArgumentFormatter.Format(summary)})");

			OnTestMethodFinished__Lambda?.Invoke();

			await base.OnTestMethodFinished(ctxt, summary);

			return OnTestMethodFinished__Result;
		}

		public async ValueTask<bool> OnTestMethodStarting()
		{
			await using var ctxt = new TestMethodRunnerContext<ITestMethod, ITestCase>(TestMethod, [testCase], ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await OnTestMethodStarting(ctxt);
		}

		public Action? OnTestMethodStarting__Lambda = null;
		public bool OnTestMethodStarting__Result = true;

		protected override async ValueTask<bool> OnTestMethodStarting(TestMethodRunnerContext<ITestMethod, ITestCase> ctxt)
		{
			Invocations.Add("OnTestMethodStarting");

			OnTestMethodStarting__Lambda?.Invoke();

			await base.OnTestMethodStarting(ctxt);

			return OnTestMethodStarting__Result;
		}

		public async ValueTask<RunSummary> RunAsync()
		{
			await using var ctxt = new TestMethodRunnerContext<ITestMethod, ITestCase>(TestMethod, [testCase], ExplicitOption.Off, MessageBus, Aggregator, CancellationTokenSource);
			await ctxt.InitializeAsync();

			return await Run(ctxt);
		}

		public RunSummary RunTestCaseAsync__Result = new();

		protected override ValueTask<RunSummary> RunTestCase(
			TestMethodRunnerContext<ITestMethod, ITestCase> ctxt,
			ITestCase testCase)
		{
			Invocations.Add($"RunTestCaseAsync(testCase: \"{testCase.TestCaseDisplayName}\")");

			return new(RunTestCaseAsync__Result);
		}

		static string TypeName(object? obj) =>
			obj is null ? "null" : $"typeof({ArgumentFormatter.FormatTypeName(obj.GetType())})";
	}
}
