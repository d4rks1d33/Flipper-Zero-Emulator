#
# Copyright (c) 2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#

import xmltodict
from RenodeModelsCompare.registers.register import RegistersGroup, Field, Register, _wrap_in_list
from RenodeModelsCompare.registers.converter import BaseConverter

class SvdConverter(BaseConverter):

    def __init__(self, peripheral_name: str) -> None:
        self.peripheral_name = peripheral_name

    def _convert_field(self, field_fragment: 'svd|dict', uniq_id: int, reg_access: str = '???'):
        access = field_fragment.get('access') or reg_access

        def _map_svd_access_to_renode_access(access: str) -> str:
            # TODO: this list definitely needs to be expanded but we will get KeyError here, so we will know
            return {
                'read-only'  : 'Read',
                'write-only' : 'Write',
                'read-write' : 'Read, Write',
            }[access]

        def _get_start(fragment):
            if 'bitOffset' in fragment:
                return int(field_fragment['bitOffset'], 10)
            elif 'lsb':
                return int(field_fragment['lsb'], 10)
            else:
                raise LookupError('RangeStart')

        def _get_end(fragment):
            if 'bitWidth' in fragment:
                return int(field_fragment['bitOffset'], 10) + int(field_fragment['bitWidth'], 10) - 1
            elif 'msb':
                return int(field_fragment['msb'], 10)
            else:
                raise LookupError('RangeEnd')

        return Field({
            'UniqueId':         uniq_id,
            'Name':             ((r := field_fragment.get('displayName')) and r.strip()) or field_fragment['name'].strip(),
            'Description':      field_fragment.get('description'),
            'Range': {
                'Start':        _get_start(field_fragment),
                'End':          _get_end(field_fragment)
            },
            'BlockId':          0,      # does not translate
            'GeneratorName':    None,   # neither this - PROPOSAL far future TODO: put code here that would generate the register in Renode
            'SpecialKind':      '',
            'CallbackInfo':     dict(),
            'FieldMode':        _map_svd_access_to_renode_access(access),
        })

    def _convert_register(self, register: 'svd|dict') -> 'Register':
        def _get_fields_from_svd_fragment(fields_svd_fragment: 'svd|dict', reg_access: str = '???') -> dict:
            rets = []
            id = 0
            for field in fields_svd_fragment:
                rets.append(self._convert_field(field, id, reg_access))
                id += 1

            return rets

        def _int_or_none(fragment, alt=None):
            if fragment in register:
                return int(register[fragment], 16)
            return alt

        def get_width(fragment):
            if 'size' in fragment:
                return _int_or_none('size')
            elif 'width' in fragment:
                return _int_or_none('width')
            else:
                return 64 # FIXME nrf52840.svd!!!

        return Register({
                'Name':         ((r := register.get('displayName')) and r.strip()) or register['name'].strip(),
                'Description':  register.get('description'),
                'Address':      int(register['addressOffset'], 16),
                'Width':        get_width(register),
                'ResetValue':   _int_or_none('resetValue'),
                'ParentReg':    None,    # TODO: dim, dimIncrement - used to denote derivative registers (DefineMany)
                'SpecialKind':  '',      # none currently
                'CallbackInfo': dict(),
                'Fields':       _get_fields_from_svd_fragment(_wrap_in_list(register['fields']['field']), register.get('access', '???')),
        })

    def convert_from(self, file_path: str) -> RegistersGroup:
        registers = None

        with open(file_path, 'r') as file:
            svd = xmltodict.parse(file.read())

        for peripheral in _wrap_in_list(svd['device']['peripherals']['peripheral']):
            if peripheral['name'] == self.peripheral_name:
                if registers:
                    print('Many peripherals of the same name, SVD is malformed. Selecting the first one.')
                    break
                registers = []

                for register in _wrap_in_list(peripheral['registers']['register']):
                    registers.append(self._convert_register(register))

        # we extracted no data
        if registers is None:
            raise LookupError(f'The peripheral named "{self.peripheral_name}" does not exist in this file.')

        return RegistersGroup(registers, peripheral_name=self.peripheral_name)

    def convert_to(self, reg_group: RegistersGroup) -> str:
        raise NotImplementedError('Conversion to SVD is not implemented')
