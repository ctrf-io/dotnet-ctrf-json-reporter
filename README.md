# Dotnet JSON Reporter

> Save MSTest Nunit Xunit test results as a JSON file

A Dotnet MSTest Nunit Xunit JSON test reporter to create test reports that follow the CTRF standard.

[Common Test Report Format](https://ctrf.io) ensures the generation of uniform JSON test reports, independent of programming languages or test framework in use.

<div align="center">
<div style="padding: 1.5rem; border-radius: 8px; margin: 1rem 0; border: 1px solid #30363d;">
<span style="font-size: 23px;">💚</span>
<h3 style="margin: 1rem 0;">CTRF tooling is open source and free to use</h3>
<p style="font-size: 16px;">Support the project by giving it a follow and a star ⭐</p>

<div style="margin-top: 1.5rem;">
<a href="https://github.com/ctrf-io/dotnet-ctrf-json-reporter">
<img src="https://img.shields.io/github/stars/ctrf-io/dotnet-ctrf-json-reporter?style=for-the-badge&color=2ea043" alt="GitHub stars">
</a>
<a href="https://github.com/ctrf-io">
<img src="https://img.shields.io/github/followers/ctrf-io?style=for-the-badge&color=2ea043" alt="GitHub followers">
</a>
</div>
</div>

<p style="font-size: 14px; margin: 1rem 0;">
Maintained by <a href="https://github.com/ma11hewthomas">Matthew Thomas</a><br/>
Contributions are very welcome! <br/>
Explore more <a href="https://www.ctrf.io/integrations">integrations</a>
</p>
</div>

## Features

- Generate JSON test reports that are [CTRF](https://ctrf.io) compliant
- Straightforward integration with MSTest, NUnit and Xunit
- Convert TRX files to JSON

```json
{
  "results": {
    "tool": {
      "name": "mstest"
    },
    "summary": {
      "tests": 2,
      "passed": 1,
      "failed": 1,
      "pending": 0,
      "skipped": 0,
      "other": 0,
      "start": 1706828654274,
      "stop": 1706828655782
    },
    "tests": [
      {
        "name": "ctrf should generate the same report with any tool",
        "status": "passed",
        "duration": 100,
        "start": 1706828654500,
        "stop": 1706828654600,
        "suite": "UnitTests",
        "rawStatus": "Passed",
        "filePath": "/path/to/test.cs"
      },
      {
        "name": "failing test example",
        "status": "failed",
        "duration": 50,
        "start": 1706828654600,
        "stop": 1706828654650,
        "suite": "UnitTests",
        "message": "Expected: True\nActual: False",
        "trace": "   at FailingTest() in /path/to/test.cs:line 42",
        "line": 42,
        "rawStatus": "Failed",
        "filePath": "/path/to/test.cs"
      }
    ],
    "environment": {
      "appName": "MyApp",
      "buildName": "MyBuild",
      "buildNumber": "1"
    }
  }
}
```

## What is CTRF?

CTRF is a universal JSON test report schema that addresses the lack of a standardized format for JSON test reports.

**Consistency Across Tools:** Different testing tools and frameworks often produce reports in varied formats. CTRF ensures a uniform structure, making it easier to understand and compare reports, regardless of the testing tool used.

**Language and Framework Agnostic:** It provides a universal reporting schema that works seamlessly with any programming language and testing framework.

**Facilitates Better Analysis:** With a standardized format, programatically analyzing test outcomes across multiple platforms becomes more straightforward.

## Installation

Create a tool manifest (if not already present):

```bash
dotnet new tool-manifest
```

Install DotnetCtrfJsonReporter as a local tool:

```bash
dotnet tool install DotnetCtrfJsonReporter --local
```

Run your tests and generate a TRX file using the following command:

```bash
dotnet test --logger "trx;logfilename=testResults.trx"
```

After the tests have completed, run DotnetCtrfJsonReporter to convert the TRX file into a CTRF:

```bash
dotnet tool run DotnetCtrfJsonReporter -t "TestResults/testResults.trx"
```

You'll find a JSON file named `ctrf-report.json` in the `ctrf` directory.

## Reporter Options

The reporter supports several configuration options:

-p, --trx-path (required): The path to the TRX file generated by dotnet test.

-f, --output-filename (optional): Name of the output JSON file. Default is ctrf-report.json.

-d, --output-directory (optional): Directory where the JSON report will be saved. Default is the ctrf directory.

-t --test-tool (optional): Name of the test tool (nunit, mstest, xunit, etc.). Default is dotnet

```bash
dotnet tool run DotnetCtrfJsonReporter \
-p "TestResults/testResults.trx" \
-f "custom-report.json" \
-d "custom-directory" \
-t "mstest"
```

## Test Object Properties

The test object in the report includes the following [CTRF properties](https://ctrf.io/docs/specification/test):

| Name         | Type   | Required | Details                                                                             |
| ------------ | ------ | -------- | ----------------------------------------------------------------------------------- |
| `name`       | String | Required | The name of the test.                                                               |
| `status`     | String | Required | The outcome of the test. One of: `passed`, `failed`, `skipped`, `pending`, `other`. |
| `duration`   | Number | Required | The time taken for the test execution, in milliseconds.                             |
| `message`  | String | Optional | A descriptive message or note associated with the test result.                        |
| `trace`    | String | Optional | The stack trace captured if the test failed.                                          |
| `start`      | Number | Optional | Test start time as epoch milliseconds.                                              |
| `stop`       | Number | Optional | Test end time as epoch milliseconds.                                                |
| `suite`      | String | Optional | The test suite or class name (extracted from TestMethod className).                 |
| `line`       | Number | Optional | Line number where the test failure occurred (extracted from stack trace).           |
| `rawStatus`  | String | Optional | Original TRX outcome status (e.g., "Passed", "Failed", "NotExecuted").              |
| `filePath`   | String | Optional | Path to the test source file (extracted from stack trace or codeBase).              |

## Support Us

If you find this project useful, consider giving it a GitHub star ⭐ It means a lot to us.
