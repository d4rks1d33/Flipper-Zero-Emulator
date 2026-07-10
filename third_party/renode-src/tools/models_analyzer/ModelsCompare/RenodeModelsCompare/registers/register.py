#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import abc
import json
import xmltodict
import os
import pathlib
from typing import Any, Iterator, List

def _wrap_in_list(obj: Any) -> List[Any]:
    return [obj] if not isinstance(obj, list) else obj

def to_json_simple(obj: Any) -> str:
    if hasattr(obj, 'to_json'):
        return obj.to_json()
    raise TypeError(f'Object of type {type(obj).__name__} does not contain to_json method')

def probe_is_file_svd(file_path: str) -> bool:
    # syntax for selecting peripheral within svd
    if ':' in file_path:
        (file_path) = file_path.split(':')[0]
    with open(file_path, 'r') as filep:
        first_line = filep.readline()
        return '<?xml' in first_line
    
def svd_get_peripheral_names(file_path: str) -> Iterator[str]:
    with open(file_path, 'r') as file:
        svd = xmltodict.parse(file.read())

    for peripheral in _wrap_in_list(svd['device']['peripherals']['peripheral']):
        yield peripheral['name']

class _CommonBase(abc.ABC):
    def get_kinds(self) -> List[str]:
        return list(map(lambda s: s.strip(), self.raw_data['SpecialKind'].split(',')))

    def is_any_special_kind(self, *kinds: str) -> bool:
        reg_kinds = self.get_kinds()
        return any(kind in reg_kinds for kind in kinds)

    def get_width(self) -> 'int|None':
        if self.is_any_special_kind('VariableLength', 'VariablePosition'):
            return None
        return self.raw_data["Range"]["End"] - self.raw_data["Range"]["Start"] + 1

    def get_callback_info(self) -> List[str]:
        shorts = {
            "HasReadCb":            'Read',
            "HasWriteCb":           'Write',
            "HasChangeCb":          'Change',
            "HasValueProviderCb":   'Provider',
        }
        rets = []
        for short, present in self.raw_data['CallbackInfo'].items():
            if present:
                rets.append(shorts[short])

        return rets

#NOTE: if using _CommonBase store dict in variable named raw_data
class Field(_CommonBase):
    def __init__(self, field: dict) -> None:
        self.raw_data = field

    def __getitem__(self, key: str) -> dict:
        return self.raw_data[key]

    def to_json(self) -> dict:
        return self.raw_data
    
    @property
    def Start(self) -> int:
        return self.raw_data['Range']['Start']

    @property
    def End(self) -> int:
        return self.raw_data['Range']['End']

    def get_field_modes_str(self) -> str:
        if not (modes := self.get_field_modes()):
            return 'N/A'
        else:
            return (', '.join(modes)) or (self.raw_data["FieldMode"])

    # TODO: enum ???
    def get_field_modes(self) -> 'List[str]':
        if self.is_any_special_kind('Tag', 'Reserved'):
            return []
        return [s.strip().replace('FieldMode.', '') for s in self.raw_data["FieldMode"]]

class Register(_CommonBase):
    def __init__(self, reg: dict) -> None:
        self.raw_data = reg

    def __getitem__(self, key: str) -> dict:
        return self.raw_data[key]

    def to_json(self) -> dict:
        return self.raw_data

    @property
    def Offset(self) -> int:
        return self.raw_data['Address']

    @property
    def Name(self) -> str:
        return self.raw_data['Name']

    @property
    def Fields(self) -> dict:
        return self.raw_data['Fields']
    
    def get_width(self) -> 'int|None':
        if self.is_any_special_kind('VariableLength', 'VariablePosition'):
            return None
        return self.raw_data['Width']

    @classmethod
    def from_json_fragment(cls, register: dict) -> 'Register':
        register_cpy = register.copy()
        fields = [Field(f) for f in register['Fields']]
        register_cpy['Fields'] = fields

        return cls(register_cpy)

class RegistersGroup:
    def __init__(self, registers: List['Register|dict'], *, group_name: str = 'Registers', peripheral_name: str = '') -> None:
        self.raw_data = dict()
        self.raw_data['Name'] = group_name
        self.raw_data['Registers'] = sorted(registers, key = lambda r: r.Offset)
        self.peripheral_name = peripheral_name

    def __getitem__(self, key: str) -> Register:
        return self.raw_data['Registers'][key]

    def __len__(self) -> int:
        return len(self.raw_data['Registers'])

    def __iter__(self) -> 'RegistersGroup':
        self._iter_idx = 0
        return self

    def __next__(self) -> dict:
        if self._iter_idx >= len(self.raw_data['Registers']):
            raise StopIteration()
        self._iter_idx += 1
        return self.raw_data['Registers'][self._iter_idx - 1]

    @property
    def GroupName(self) -> str:
        return self.raw_data['Name']

    @property
    def PeripheralName(self) -> str:
        return self.peripheral_name

    @property
    def Registers(self) -> List['dict|Register']:
        return [Register(e) for e in self.raw_data['Registers']]

    def to_json_file(self, file_path: str) -> None:
        with open(file_path, 'w') as file:
            json.dump([self.raw_data], file, default=to_json_simple, indent=2)

    @classmethod
    def from_json_file(cls, file_path: str) -> 'List[RegistersGroup]':
        with open(file_path, 'r') as filep:
            regs = json.load(filep)

        rets = []
        for group in regs:
            peripheral_name = pathlib.Path(file_path).name.split('.', 1)[0].split('-')[0]
            rets += [cls([Register.from_json_fragment(r) for r in group['Registers']], 
                         group_name=group['Name'], peripheral_name=peripheral_name)]
        return rets

    @classmethod
    def from_svd_file(cls, file_path: str, peripheral_name: str) -> 'RegistersGroup':
        from RenodeModelsCompare.registers.svd_converter import SvdConverter

        return SvdConverter(peripheral_name).convert_from(file_path)

    def to_systemrdl_file(self, file_path: str, **kwargs) -> None:
        from RenodeModelsCompare.registers.systemrdl_converter import SystemRDLConverter

        converter = SystemRDLConverter(**kwargs)
        with open(file_path, 'w') as file:
            file.write(f'addrmap {pathlib.Path(self.PeripheralName)} {{\n\n')
            file.write(converter.convert_to(self))
            file.write('\n\n};')


    @classmethod
    def from_systemrdl_file(cls, file_path: str, **kwargs) -> 'RegistersGroup':
        from RenodeModelsCompare.registers.systemrdl_converter import SystemRDLConverter

        converter = SystemRDLConverter(**kwargs)
        return converter.convert_from(file_path)
