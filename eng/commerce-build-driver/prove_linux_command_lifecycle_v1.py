"""Actual Linux pipe/child proof; invokes Python fixtures, never the SDK build stage."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import traceback
import unittest
from types import SimpleNamespace
from unittest.mock import patch
import commerce_native_build_stage_v3 as driver

OWNER = '01a1009c-aa47-72d2-9914-3a0784a67c0e'
SHARED_SHA = '44a8a5accac9da11422d606be02fe28487642215df511b5f1c4284296a453ee2'


def fixture_source(scenario):
    # Default SIGALRM terminates the child independently of blocked stdout writes.
    timer = 'import signal; signal.signal(signal.SIGALRM,signal.SIG_DFL); signal.setitimer(signal.ITIMER_REAL,60); '
    if scenario == 'normal':
        return timer + "import os,time; time.sleep(0.2); os.write(1,b'owned-python-fixture\\n')"
    if scenario == 'overflow':
        return timer + "import os,time; [os.write(1,b'x'*65536) for _ in range(129)]; time.sleep(60)"
    if scenario in ('timeout', 'registration-cancel'):
        return timer + 'import time; time.sleep(60)'
    raise ValueError('Unknown scenario')


class FixtureLeaseControls(unittest.TestCase):
    def test_independent_child_timer_is_installed_before_any_blocking_action(self):
        for scenario in ('normal', 'overflow', 'timeout', 'registration-cancel'):
            with self.subTest(scenario=scenario):
                events = []
                def write(*args):
                    self.assertEqual([('handler', 14, 0), ('timer', 0, 60)], events[:2])
                    events.append(('write',))
                    raise ValueError('Simulated blocked fixture write')
                def sleep(seconds):
                    self.assertEqual([('handler', 14, 0), ('timer', 0, 60)], events[:2])
                    events.append(('sleep', seconds))
                modules = {'signal': SimpleNamespace(ITIMER_REAL=0, SIGALRM=14, SIG_DFL=0,
                    signal=lambda kind, handler: events.append(('handler', kind, handler)),
                    setitimer=lambda kind, seconds: events.append(('timer', kind, seconds))),
                    'os': SimpleNamespace(write=write), 'time': SimpleNamespace(sleep=sleep)}
                with patch.dict(sys.modules, modules):
                    if scenario in ('normal', 'overflow'):
                        with self.assertRaisesRegex(ValueError, 'blocked fixture write'):
                            exec(fixture_source(scenario), {})
                    else:
                        exec(fixture_source(scenario), {})
                self.assertEqual([('handler', 14, 0), ('timer', 0, 60)], events[:2])


def prove_scenario(shared, results, scenario):
    workers, selectors, ledger = [], [], []
    popen = subprocess.Popen
    selector_type = driver.selectors.DefaultSelector
    descriptors_before = len(list(Path('/proc/self/fd').iterdir()))
    class TrackedSelector(selector_type):
        def __init__(self):
            super().__init__()
            self.actual_closed = False
            selectors.append(self)
        def register(self, *args, **kwargs):
            result = super().register(*args, **kwargs)
            if scenario == 'registration-cancel':
                raise KeyboardInterrupt('Actual registered-pipe cancellation fixture')
            return result
        def close(self):
            super().close()
            self.actual_closed = True
    def start(*args, **kwargs):
        child = popen(*args, **kwargs)
        workers.append(child)
        return child
    source = fixture_source(scenario)
    expected = None
    seconds = 20
    if scenario == 'normal':
        pass
    elif scenario == 'overflow':
        expected = ValueError
    elif scenario == 'timeout':
        seconds = 0.2
        expected = TimeoutError
    elif scenario == 'registration-cancel':
        expected = KeyboardInterrupt
    else:
        raise ValueError('Unknown scenario')
    outcome = None
    try:
        with patch.object(driver.subprocess, 'Popen', side_effect=start), \
                patch.object(driver.selectors, 'DefaultSelector', TrackedSelector):
            try:
                output = driver.command(shared, [sys.executable, '-B', '-c', source],
                    results, results / (scenario + '.log'), seconds, ledger)
            except BaseException as error:
                outcome = error
        if expected is None:
            if outcome is not None: raise outcome
            if output != 'owned-python-fixture\n': raise ValueError('Actual EOF capture differs')
        elif type(outcome) is not expected:
            raise ValueError('Expected first failure differs: ' + repr(outcome))
        if scenario == 'overflow' and 'during execution' not in str(outcome):
            raise ValueError('Overflow was not observed during execution')
        if len(workers) != 1 or len(ledger) != 1 or len(selectors) != 1:
            raise ValueError('Actual ownership inventory differs')
        child, record = workers[0], ledger[0]
        if child.poll() is None or not child.stdout.closed or not selectors[0].actual_closed:
            raise ValueError('Actual child/pipe/selector not settled')
        if Path('/proc/' + str(child.pid)).exists():
            raise ValueError('Actual process PID still present; preserve uncertain identity')
        if not record['directChildExited'] or record['pid'] != child.pid or not record['startTicks']:
            raise ValueError('Actual process receipt incomplete')
        if hasattr(child, '_stdout_thread') or hasattr(child, '_stderr_thread'):
            raise ValueError('Unexpected reader thread allocated')
        if (results / (scenario + '.log')).stat().st_size > driver.MAX_LOG_BYTES:
            raise ValueError('Actual retained log exceeds fixed active cap')
        if scenario == 'overflow' and (results / (scenario + '.log')).stat().st_size != driver.MAX_LOG_BYTES:
            raise ValueError('Actual capped output differs')
        if len(list(Path('/proc/self/fd').iterdir())) != descriptors_before:
            raise ValueError('Actual descriptor count did not recover')
        return {'scenario': scenario, 'process': record, 'actualPipeClosed': True,
                'actualSelectorClosed': True, 'actualPidAbsent': True,
                'descriptorCountRecovered': True, 'readerThreadAllocated': False,
                'firstFailure': None if outcome is None else type(outcome).__name__ + ': ' + str(outcome),
                'logBytes': (results / (scenario + '.log')).stat().st_size,
                'sdkStarted': False, 'dockerStarted': False, 'nativeAccepted': False}
    finally:
        # An assertion fault cannot abandon the retained exact real process.
        pending_failure = sys.exc_info()[1]
        cancellation = None
        for child in workers:
            interruption = shared.recover_fetch_owner(child)
            if interruption is not None and cancellation is None:
                cancellation = interruption
        for selector in selectors:
            selector.close()
        (results / (scenario + '-owned-child-ledger.json')).write_text(json.dumps(ledger, indent=2))
        if cancellation is not None and pending_failure is None:
            raise cancellation


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    if sys.platform != 'linux': raise ValueError('Actual Linux proof required')
    shared = driver.verified_module(Path(__file__).parent / 'sealed_source_capsule.py', SHARED_SHA)
    shared.reject_links(args.results.absolute())
    args.results.mkdir(exist_ok=False)
    rows, failure = [], None
    try:
        for scenario in ('normal', 'overflow', 'timeout', 'registration-cancel'):
            rows.append(prove_scenario(shared, args.results, scenario))
    except BaseException as error:
        failure = type(error).__name__ + ': ' + str(error)
        (args.results / 'first-failure.txt').write_text(traceback.format_exc())
        raise
    finally:
        receipt = {'owner': OWNER, 'observedUtc': datetime.now(timezone.utc).isoformat(),
                   'sharedSourceSha256': SHARED_SHA,
                   'driverSourceSha256': hashlib.sha256(Path(driver.__file__).read_bytes()).hexdigest(),
                   'actualPythonExecutable': sys.executable,
                   'scenarios': rows, 'firstFailure': failure,
                   'sdkStarted': False, 'dockerStarted': False, 'nativeAccepted': False,
                   'outerOwnerRoutingAndAdmissionPending': True}
        (args.results / 'linux-command-lifecycle-proof.json').write_text(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()
