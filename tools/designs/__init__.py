"""tools/designs: design modules for the drone / printer blueprints (owned by the packager).

Every design module exposes  MODELS = {"<Name>": callable(lite: bool) -> sebp.Blueprint}.
The modules import each other by their top-level names (`import _common`, `from core import ...`), so this package puts its own
directory on sys.path and imports them under those top-level names.  Nothing is built at import time.

    import designs
    mods, missing = designs.load()            # {module name: module}, [module names that do not exist yet]
    for kind, modname, model, fn in designs.iter_models(mods): ...
"""
import os, sys, importlib

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.normpath(os.path.join(HERE, '..'))
ROOT = os.path.normpath(os.path.join(TOOLS, '..'))
for _p in (HERE, TOOLS):
    if _p not in sys.path: sys.path.insert(0, _p)

DRONE_MODULES = ('core', 'wasp', 'support', 'extra')
PRINTER_MODULES = ('printers',)
ALL_MODULES = DRONE_MODULES + PRINTER_MODULES

def load(names=ALL_MODULES):
    """import the design modules; returns ({name: module}, [names whose file does not exist])"""
    mods, missing = {}, []
    for n in names:
        if not os.path.exists(os.path.join(HERE, n + '.py')):
            missing.append(n); continue
        mods[n] = importlib.import_module(n)
    return mods, missing

def iter_models(mods):
    """yield (kind, module name, model name, builder) in a stable order; kind is 'drone' or 'printer'"""
    for n in ALL_MODULES:
        if n not in mods: continue
        kind = 'drone' if n in DRONE_MODULES else 'printer'
        for model, fn in mods[n].MODELS.items():
            yield kind, n, model, fn
