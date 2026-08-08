using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WpfMcp.Core;
using WpfMcp.Core.Server;

namespace WpfMcp.ExampleApp
{
    [McpToolCollection]
    public static partial class ServerTools
    {
        [McpTool("create_sum")]
        [Description("Adds two numbers together")]
        public static int Sum([Description("First value")] int a, [Description("Second value")] int b)
        {
            return a + b;
        }

        [McpTool("create_sum_long")]
        [Description("Adds two numbers, taking a couple of seconds about it")]
        public static async Task<int> LongSumCalculation(
            [Description("First value")] int a,
            [Description("Second value")] int b,
            CancellationToken cancellationToken)
        {
            await Task.Delay(2000, cancellationToken);

            return a + b;
        }

        [McpTool("fail_on_purpose")]
        [Description("Always throws, to demonstrate how tool failures are reported")]
        public static string FailOnPurpose()
        {
            throw new InvalidOperationException("This tool fails by design.");
        }

        [McpTool("count_slowly")]
        [Description("Counts up to a number, reporting progress along the way")]
        public static async Task<int> CountSlowly(
            [Description("How high to count")] int steps,
            IMcpProgress progress,
            CancellationToken cancellationToken)
        {
            for (int i = 1; i <= steps; i++)
            {
                await Task.Delay(300, cancellationToken);
                await progress.ReportAsync(i, steps, $"Step {i} of {steps}");
            }

            return steps;
        }
    }
}