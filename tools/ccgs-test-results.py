"""Require actual, nonempty passing Unity test results; absent suites are not a pass."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
failed = False
for filename in sys.argv[1:]:
    path = Path(filename)
    try:
        root = ET.parse(path).getroot()
        tests = list(root.iter('test-case'))
        bad = [t for t in tests if t.get('result') not in ('Passed', 'Success')]
        ok = bool(tests) and not bad and root.get('result') in ('Passed', 'Success')
        print(f'{path}: {len(tests)} tests; {"PASS" if ok else "NOT PASSED"}')
        failed |= not ok
    except (OSError, ET.ParseError) as exc:
        print(f'{path}: UNVERIFIED ({exc})', file=sys.stderr)
        failed = True
sys.exit(1 if failed or len(sys.argv) < 2 else 0)
