#!/usr/bin/env python3

import sys
import click
import subprocess
import pathlib
import shlex

DEBUG = False

@click.group()
def group():
    pass

@group.command()
@click.argument('cmd')
@click.argument('dir')
@click.argument('glob')
@click.argument('onsuccess', required=False)
def run_tests(**kwargs):
    dir = pathlib.Path(kwargs['dir'])

    def expand_path(path: str):
        return pathlib.Path(path.format(file=str(elem.name), sfile=sfile, path=str(elem.parent))).expanduser()

    failed = []
    total = 0
    for elem in dir.rglob(kwargs['glob']):
        if elem.is_file():
            print(f'+++++ {elem.name}')
            sfile = pathlib.Path(elem.name).stem.removesuffix('.cs-registersInfo')
            cmd = expand_path(kwargs['cmd'])

            if DEBUG:
                print('Executing:', shlex.split(str(cmd)))

            res = subprocess.run(shlex.split(str(cmd)), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8')
            
            print(res.stdout)
            
            if res.returncode != 0:
                failed.append(elem.name)
                print('++ FAILED')
            else:
                print('++ SUCCESS')
                if 'onsuccess' in kwargs and kwargs['onsuccess']:
                    onsuccess = expand_path(kwargs['onsuccess'])
                    res = subprocess.run(shlex.split(str(onsuccess)), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8')
                    print(res.stdout)

            total += 1
            sys.stdout.flush()

    print(f'Failed tests {len(failed)}/{total}.')
    print('\n *'.join(sorted(failed)))


if __name__ == '__main__':
    group()
