import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const temporary = mkdtempSync(path.join(tmpdir(), 'dotnet-ctrf-'));
const dotnet = process.env.DOTNET_COMMAND || 'dotnet';
const cli = process.env.CTRF_CLI
  ? [process.execPath, [path.resolve(process.env.CTRF_CLI)]]
  : ['npx', ['--yes', 'ctrf-cli@0.5.0']];
let reports = 0;
let rejected = 0;

function execute(command, args, expected = 0) {
  const result = spawnSync(command, args, {
    cwd: root, encoding: 'utf8',
    env: { ...process.env, TZ: 'America/Los_Angeles', LANG: 'fr_FR.UTF-8' },
  });
  assert.equal(result.status, expected, `${command} ${args.join(' ')}\n${result.error || ''}\n${result.stdout}\n${result.stderr}`);
  return result;
}
function validate(reportPath, expected = 0) {
  const result = execute(cli[0], [...cli[1], 'validate-strict', reportPath, '--spec-version', '0.2.0'], expected);
  if (expected === 0) assert.ok(result.stdout.includes('is valid CTRF (strict)'), result.stdout);
}
function convert(name, xml, options = []) {
  const input = path.join(temporary, `${name}.trx`);
  const output = path.join(temporary, `${name}.json`);
  writeFileSync(input, xml);
  execute(dotnet, ['bin/Release/net8.0/DotnetCtrfJsonReporter.dll', '-p', input, '-d', temporary, '-f', `${name}.json`, ...options]);
  validate(output);
  reports++;
  const report = JSON.parse(readFileSync(output, 'utf8'));
  assert.equal(report.specVersion, '0.2.0');
  assert.equal(report.reportFormat, 'CTRF');
  assert.equal(report.generatedBy, 'dotnet-ctrf-json-reporter');
  assert.match(report.reportId, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  assert.ok(Number.isFinite(Date.parse(report.timestamp)));
  const { summary, tests } = report.results;
  assert.equal(summary.tests, tests.length);
  for (const status of ['passed', 'failed', 'skipped', 'pending', 'other']) {
    assert.equal(summary[status], tests.filter(test => test.status === status).length);
  }
  assert.ok(summary.stop >= summary.start);
  for (const test of tests) assert.ok(Number.isInteger(test.duration) && test.duration >= 0);
  return report;
}
function reject(name, xml, options = []) {
  const input = path.join(temporary, `${name}.trx`);
  const output = path.join(temporary, `${name}.json`);
  writeFileSync(input, xml);
  execute(dotnet, ['bin/Release/net8.0/DotnetCtrfJsonReporter.dll', '-p', input, '-d', temporary, '-f', `${name}.json`, ...options], 1);
  assert.equal(existsSync(output), false, 'Invalid input must not write a report');
  rejected++;
}
const wrap = (results = '', extra = '') => `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>${results}</Results>${extra}</TestRun>`;
try {
  execute(dotnet, ['build', '-c', 'Release', '--no-restore', '-warnaserror']);
  const report = convert('mixed', readFileSync(new URL('fixtures/results.trx', import.meta.url), 'utf8'), ['-t', 'mstest']);
  assert.equal(report.results.tool.name, 'mstest');
  assert.deepEqual(report.results.summary, { tests: 9, passed: 1, failed: 4, skipped: 1, pending: 1, other: 2,
    start: Date.parse('2026-10-05T10:00:00.123Z'), stop: Date.parse('2026-10-05T10:00:05.456Z') });
  const tests = report.results.tests;
  assert.deepEqual(tests.map(test => test.duration), [100, 123, 10, 3723456, 0, 0, 0, 2592000000, 0]);
  assert.deepEqual(tests[0].suite, ['Example', 'Tests', 'UnitTests']);
  assert.deepEqual(tests[2].suite, ['UnitTests']);
  assert.deepEqual(tests[0].stdout, ['first', '', 'third', 'fourth', 'fifth']);
  assert.equal(tests[0].start, report.results.summary.start);
  assert.equal(tests[0].stop, Date.parse('2026-10-05T10:00:00.223Z'));
  assert.equal('filePath' in tests[0], false);
  assert.equal(tests[1].filePath, 'C:\\src\\UnitTests.cs');
  assert.equal(tests[1].line, 42);
  assert.equal(tests[1].message, 'Expected true');
  assert.ok(tests[1].trace.includes(':line 42'));
  assert.equal(tests[2].filePath, '/src/UnitTests.cs');
  assert.equal('suite' in tests[3], false);
  assert.equal('stdout' in tests[3], false);
  assert.deepEqual(tests.map(test => test.rawStatus), ['Passed', 'Failed', 'Error', 'Timeout', 'Aborted', 'NotExecuted', 'Pending', 'Inconclusive', 'FutureOutcome']);

  const identityXml = '<TestRun id="source-run"><Results><UnitTestResult testId="source-case" executionId="source-execution" testName="data-row-1" outcome="Passed" duration="00:00:00.001"/></Results><TestDefinitions><UnitTest id="source-case"><TestMethod className="Assembly.Namespace.Class" name="Method" codeBase="/first/Tests.dll"/></UnitTest></TestDefinitions></TestRun>';
  const identityOne = convert('identity-one', identityXml, ['--shard-id', 'one']);
  const identityTwo = convert('identity-two', identityXml.replace('/first/', '/second/'), ['--run-id', 'coordinated', '--shard-id', 'two']);
  assert.equal(identityOne.runId, 'source-run');
  assert.equal(identityTwo.runId, 'coordinated');
  assert.equal(identityOne.results.tests[0].testId, identityTwo.results.tests[0].testId);
  assert.equal(identityOne.results.tests[0].executionId, 'source-execution');
  assert.equal(identityOne.results.environment.shardId, 'one');
  assert.notEqual(identityOne.reportId, identityTwo.reportId);
  reject('empty-run-id', identityXml, ['--run-id', ' ']);
  reject('empty-shard-id', identityXml, ['--shard-id', ' ']);

  const empty = convert('empty', wrap());
  assert.equal(empty.results.summary.start, 0);
  assert.equal(empty.results.summary.stop, 0);
  assert.equal(empty.results.tool.name, 'dotnet');
  const fallback = convert('fallback', wrap('<UnitTestResult testId="id" startTime="2026-10-05T10:00:00Z" endTime="2026-10-05T10:00:01.234Z" />'));
  assert.equal(fallback.results.tests[0].name, 'id');
  assert.equal(fallback.results.tests[0].duration, 1234);
  assert.equal(fallback.results.tests[0].status, 'other');
  assert.equal(fallback.results.summary.stop - fallback.results.summary.start, 1234);

  const noOffset = convert('utc-without-offset', wrap('<UnitTestResult testName="UTC" startTime="2026-10-05T10:00:00" endTime="2026-10-05T10:00:01" />'));
  assert.equal(noOffset.results.tests[0].start, Date.parse('2026-10-05T10:00:00Z'));
  assert.equal(noOffset.results.tests[0].duration, 1000);
  const noPrecision = convert('whole-seconds', wrap('<UnitTestResult testName="whole seconds" duration="00:00:02" />'));
  assert.equal(noPrecision.results.tests[0].duration, 2000);

  const invalidTime = convert('invalid-times', wrap('<UnitTestResult testName="bad time" startTime="invalid" endTime="invalid" />', '<Times start="invalid" finish="invalid"/>'));
  assert.equal(invalidTime.results.summary.start, 0);
  assert.equal('start' in invalidTime.results.tests[0], false);
  assert.equal('stop' in invalidTime.results.tests[0], false);
  const partial = convert('partial-time', wrap('<UnitTestResult testName="partial" endTime="2026-10-05T10:00:01Z" />'));
  assert.equal(partial.results.summary.start, partial.results.tests[0].stop);
  assert.equal(partial.results.summary.stop, partial.results.tests[0].stop);
  const definition = convert('definition-name', wrap('<UnitTestResult testId="id" testName="" />', '<TestDefinitions><UnitTest id="id" name="Definition name"><TestMethod className="..." /></UnitTest></TestDefinitions>'));
  assert.equal(definition.results.tests[0].name, 'Definition name');
  assert.equal('suite' in definition.results.tests[0], false);
  const method = convert('method-name', wrap('<UnitTestResult testId="id" />', '<TestDefinitions><UnitTest id="id"><TestMethod name="Method name" /></UnitTest></TestDefinitions>'));
  assert.equal(method.results.tests[0].name, 'Method name');

  for (const duration of ['invalid', '-00:00:01', '999999999999.00:00:00'])
    reject(`bad-duration-${rejected}`, wrap(`<UnitTestResult testName="invalid" duration="${duration}" />`));
  reject('nameless', wrap('<UnitTestResult />'));
  reject('empty-tool', wrap(), ['-t', '']);
  reject('blank-tool', wrap(), ['-t', '   ']);
  reject('reversed-test', wrap('<UnitTestResult testName="reversed" startTime="2026-10-05T10:00:01Z" endTime="2026-10-05T10:00:00Z" />'));
  reject('reversed-run', wrap('', '<Times start="2026-10-05T10:00:01Z" finish="2026-10-05T10:00:00Z" />'));
  reject('invalid-xml', '<TestRun');
  execute(dotnet, ['bin/Release/net8.0/DotnetCtrfJsonReporter.dll'], 1);
  execute(dotnet, ['bin/Release/net8.0/DotnetCtrfJsonReporter.dll', '-p', path.join(temporary, 'missing.trx')], 1);
  execute(dotnet, ['bin/Release/net8.0/DotnetCtrfJsonReporter.dll', '--help']);

  // Prove the gate rejects the old output shape, rather than only testing success.
  tests[0].suite = 'UnitTests';
  const legacy = path.join(temporary, 'legacy.json');
  writeFileSync(legacy, JSON.stringify(report));
  validate(legacy, 2);
  console.log(`Passed: ${reports} reports accepted by CTRF CLI strict validation against specification 0.2.0; ${rejected} malformed inputs rejected; legacy suite rejected.`);
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
