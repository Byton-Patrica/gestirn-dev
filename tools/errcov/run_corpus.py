"""Compiles every program of corpus.py with g++ / gcc and writes all compiler output to raw.tsv.
Usage: python run_corpus.py <folder with g++.exe and gcc.exe>"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from corpus import CASES

HERE = os.path.dirname(os.path.abspath(__file__))
BIN = sys.argv[1]
GPP = os.path.join(BIN, 'g++.exe')
GCC = os.path.join(BIN, 'gcc.exe')
work = os.path.join(HERE, 'work')
os.makedirs(work, exist_ok=True)
env = dict(os.environ, LC_ALL='C', LANG='C', PATH=BIN + ';' + os.environ.get('PATH', ''))
CPP = ('cpp', 'cpp98', 'cpp11', 'cpp_extra', 'cpp_shadow', 'linklib')

out = []
for name, lang, code in CASES:
    ext = '.cpp' if lang in CPP else '.c'
    src = os.path.join(work, name + ext)
    with open(src, 'w', encoding='utf-8', newline='\n') as f:
        f.write(code)
    exe = os.path.join(work, name + '.exe')
    if ext == '.c':
        cmd = [GCC, '-std=c89' if lang == 'c89' else '-std=c11', '-Wall', src, '-o', exe]
    else:
        std = {'cpp98': '-std=c++98', 'cpp11': '-std=c++11'}.get(lang, '-std=c++14')
        extra = ['-Wextra'] if lang == 'cpp_extra' else ['-Wshadow'] if lang == 'cpp_shadow' else []
        cmd = [GPP, std, '-Wall', '-finput-charset=UTF-8'] + extra + [src, '-o', exe]
        if lang == 'linklib':
            cmd += ['-lnosuchlib']
    r = subprocess.run(cmd, capture_output=True, env=env, cwd=work, timeout=120)
    for line in (r.stdout + r.stderr).decode('utf-8', 'replace').replace('\r', '').split('\n'):
        out.append(name + '\t' + line)
with open(os.path.join(HERE, 'raw.tsv'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(out))
print(len(CASES), 'programs,', len(out), 'output lines')
