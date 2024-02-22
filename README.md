# Dotnet JSON Reporter

> Save MSTest Nunit Xunit test results as a JSON file

A Dotnet MSTest Nunit Xunit JSON test reporter to create test reports that follow the CTRF standard.

[Common Test Report Format](https://ctrf.io) ensures the generation of uniform JSON test reports, independent of programming languages or test framework in use.

## Features

- Generate JSON test reports that are [CTRF](https://ctrf.io) compliant
- Straightforward integration with MSTest, NUnit and Xunit
- Convert TRX files to JSON

## What is CTRF?

CTRF is a universal JSON test report schema that addresses the lack of a standardized format for JSON test reports.

**Consistency Across Tools:** Different testing tools and frameworks often produce reports in varied formats. CTRF ensures a uniform structure, making it easier to understand and compare reports, regardless of the testing tool used.

**Language and Framework Agnostic:** It provides a universal reporting schema that works seamlessly with any programming language and testing framework.

**Facilitates Better Analysis:** With a standardized format, programatically analyzing test outcomes across multiple platforms becomes more straightforward.

```json
{
  "results": {
    "tool": {
      "name": "mstest"
    },
    "summary": {
      "tests": 1,
      "passed": 1,
      "failed": 0,
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
        "duration": 100
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

The test object in the report includes the following [CTRF properties](https://ctrf.io/docs/schema/test):

| Name       | Type   | Required | Details                                                                             |
| ---------- | ------ | -------- | ----------------------------------------------------------------------------------- |
| `name`     | String | Required | The name of the test.                                                               |
| `status`   | String | Required | The outcome of the test. One of: `passed`, `failed`, `skipped`, `pending`, `other`. |
| `duration` | Number | Required | The time taken for the test execution, in milliseconds.                             |
