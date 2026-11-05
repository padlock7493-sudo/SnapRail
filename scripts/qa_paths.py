"""Resolve explicit test profiles without touching the user's ordinary library."""
import json
import os
from pathlib import Path

def settings_root():
    profile=os.environ.get('SNAPRAIL_TEST_PROFILE')
    if profile:return Path(profile)
    exe=os.environ.get('SNAPRAIL_TEST_EXE')
    if exe and (Path(exe).parent/'SnapRail.portable').exists():return Path(exe).parent
    return Path(os.environ['LOCALAPPDATA'])/'SnapRail'

def data_root():
    explicit=os.environ.get('SNAPRAIL_TEST_DATA_ROOT')
    if explicit:return Path(explicit)
    root=settings_root();file=root/'settings.json'
    if not file.exists():return root
    path=Path(json.loads(file.read_text(encoding='utf-8-sig')).get('DataRoot','.'))
    return path if path.is_absolute() else root/path

def launch_args(exe):
    args=[str(exe)]
    if os.environ.get('SNAPRAIL_TEST_PROFILE'):args+=['--profile',os.environ['SNAPRAIL_TEST_PROFILE']]
    return args
