#
# Copyright (c) 2023-2025 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import abc
import re
from bidict import bidict
from textwrap import indent
from typing import List
from dataclasses import dataclass, field
from RenodeModelsCompare.registers.register import RegistersGroup, Field, Register
from RenodeModelsCompare.registers.converter import BaseConverter

def validate_rdl(file: str) -> bool:
    try:
        from systemrdl import RDLCompiler, RDLCompileError
    except ImportError:
        raise RuntimeError('You have to install SystemRDL compiler to use this feature. ' \
                'Visit https://systemrdl-compiler.readthedocs.io/en/stable/index.html for further instructions.')
    
    compiler = RDLCompiler()
    try:
        # if we have alternative layout, by default select 0
        compiler.compile_file(file, defines={'VARIANT0': '1'})
        compiler.elaborate()
    except RDLCompileError as e:
        print(e)
        return False
    
    return True

class SystemRDLConverter(BaseConverter):
    uniq_name_counter = 0
    header = []

    def __init__(self, layout_id = -1, fill_empty_registers = False, unwind_array = False) -> None:
        # Alternate layout id to convert, -1 means all at once
        self.active_layout_id = layout_id
        # Whether to fill empty register with a dummy field, so it shows in SystemRDL output
        self.fill_empty_registers = fill_empty_registers
        # Whether to unwind array (DefineMany) of registers. SystemRDL has problems with interleaving arrays
        # in that scenario it might be necessary to unwind.
        # TODO: Alternative approach would be to pack these registers into regfile, and do an array on regfile
        # but that would require checking if all parameters (step, size) stay the same everywhere
        # and unwind automatically if not
        self.unwind_array = unwind_array

    @classmethod
    def _sanitize_name(cls, name: str) -> str:
        if not name:
            cls.uniq_name_counter += 1
            return f'No_name_{cls.uniq_name_counter}'

        # Identifiers beginning with a number are illegal, so just add underscore at the beginning        
        if name[0] >= '0' and name[0] <= '9':
            name = '_' + name

        # Filter other occurring illegal symbols
        return re.sub('[^a-zA-Z0-9\_]', '', re.sub('[ \.\-/:]', '_', name))

    class RDLBlock(abc.ABC):
        def __str__(self) -> str:
            raise NotImplementedError()

    @dataclass
    class RDLConditionalBlock(RDLBlock):
        conditions_and_action: List['ConditionAndAction'] = field(default_factory=list)
        default: 'SystemRDLConverter.RDLBlock' = ''
        omit_if_possible: bool = False

        @dataclass
        class ConditionAndAction:
            condition: str
            actions: List['SystemRDLConverter.RDLBlock'] = field(default_factory=list)

        def __str__(self) -> str:
            rets = ''
            is_if = True

            if not self.conditions_and_action:
                return ''

            if len(self.conditions_and_action) == 1 and not self.default and self.omit_if_possible:
                rets += '\n'.join(indent(str(f), '    ') for f in self.conditions_and_action[0].actions)
                return rets

            for cond_act in self.conditions_and_action:
                rets += f'`{"ifdef" if is_if else "elsif"} {cond_act.condition}\n'
                rets += indent('\n'.join(str(f) for f in cond_act.actions), '    ') + '\n'

                is_if = False

            if self.default:
                rets += '`else'
                rets += indent(str(self.default), '    ') + '\n'

            rets += '`endif\n'

            return rets

        def add_cond_act_pair(self, condition: str, act: List['SystemRDLConverter.RDLBlock']) -> None:
            if not isinstance(act, list):
                raise ValueError('Wrap action in list object')

            self.conditions_and_action.append(self.ConditionAndAction(condition, act))

        def add_action_to_cond(self, condition: str, act: List['SystemRDLConverter.RDLBlock'], add_if_nonexistent: bool = True) -> None:
            if not isinstance(act, list):
                raise ValueError('Wrap action in list object')
            
            if cond_act := next(filter(lambda x: x.condition == condition, self.conditions_and_action), None):
                cond_act.actions.extend(act)
                return

            if add_if_nonexistent:
                self.add_cond_act_pair(condition, act)
                return

            raise LookupError('No such condition to add')

    @dataclass
    class RDLField(RDLBlock):
        start: int
        end: int
        name: str
        sw_access: str = 'na'
        sw_onwrite: str = ''
        sw_onread: str = ''
        hw_reset_val: str = ''
        description: List[str] = field(default_factory=list)
        block_id: int = 0
        erroneus: str = ''
        def __str__(self) -> str:

            rets = ""
            broken = len(self.erroneus) > 0

            if broken:
                rets += f'/* Conversion error: {self.erroneus}\n'

            rets += f'field {{\n'

            if self.description:
                rets += f'    desc = "{" ".join(self.description)}";\n'

            rets += f'    sw = {self.sw_access};\n'

            if self.sw_onwrite:
                rets += f'    onwrite = {self.sw_onwrite};\n'
            if self.sw_onread:
                rets += f'    onread = {self.sw_onread};\n'
            if self.hw_reset_val:
                bin_format = format(int(self.hw_reset_val), "b")
                rets += f"    reset={int(self.end) - int(self.start) + 1 or len(bin_format)}'b{bin_format};\n"

            rets += f'}} {SystemRDLConverter._sanitize_name(self.name)}[{self.end}:{self.start}];'

            if broken:
                rets += "\n*/"

            return rets

    @dataclass
    class RDLRegister(RDLBlock):
        identifier: str
        name: str
        regwidth: int
        offset: int
        fields: 'List[SystemRDLConverter.RDLField | SystemRDLConverter.RDLConditionalBlock]'
        description: List[str] = field(default_factory=list)

        stride: int = 0
        length: int = 0

        needs_external: bool = False
        erroneus: str = ''

        def __str__(self) -> str:
            if not self.fields:
                raise RuntimeError('SystemRDL requires for a register to have at least one field')

            rets = ''
            broken = len(self.erroneus) > 0

            if broken:
                rets += f'/* Conversion error: {self.erroneus}\n'

            rets += f'{"external " if self.needs_external else ""}reg {{\n'
            rets += f'    name="{self.name}";\n'

            if self.description:
                rets += f'    desc = "{" ".join(self.description)}";\n'

            if self.regwidth:
                rets += f'    regwidth={hex(self.regwidth)};\n'

            if not broken:
                if isinstance(self.fields, SystemRDLConverter.RDLConditionalBlock):
                    rets += str(self.fields)
                else:
                    rets += '\n'.join(indent(str(f), '    ') for f in self.fields)
            rets += f'\n}} {SystemRDLConverter._sanitize_name(self.identifier)} '

            if self.length > 0:
                rets += f'[{self.length}]'

            rets += f'@ {hex(self.offset)}'

            if self.stride > 0:
                rets += f' += {self.stride}'

            rets += ';'

            if broken:
                rets += "\n*/"

            return rets

    @staticmethod
    def get_sw_access(field: Field, access_modes: 'List[str]') -> str:
        if field.is_any_special_kind('Tag'):
            return 'rw'
        if not access_modes:
            return 'na'

        rets = []

        try:
            for mode in access_modes:
                rets += {
                    'Read' : 'r',
                    'ReadToClear' : 'r', # these appear to be mutually exclusive
                    'Set': 'w',
                    'Toggle': 'w',
                    'WriteZeroToClear': 'w',
                    'WriteOneToClear': 'w',
                    'Write': 'w',       # and these too
                }[mode]
        except KeyError:
            return 'user'
        return ''.join(rets)

    renode_onwrite = bidict({
                        'WriteOneToClear': 'woclr',
                        'WriteZeroToClear': 'wzc',
                        'Set': 'woset',
                        'Toggle': ' wot',
                    })

    @staticmethod
    def get_sw_write_access_properties(access_modes: 'List[str]', inverse: bool = False) -> str:
        for mode in access_modes:
            # we use assumption that read and write flags are mutually exclusive (same as in SystemBus logic in Renode, and comment in FieldMode.cs)
            try:
                if inverse:
                    return SystemRDLConverter.renode_onwrite.inverse[mode]
                else:
                    return SystemRDLConverter.renode_onwrite[mode]
            except KeyError:
                pass
        
        return ''

    renode_onread = bidict({
                        'ReadToClear': 'rclr',
                    })

    @staticmethod
    def get_sw_read_access_properties(access_modes: 'List[str]', inverse: bool = False) -> str:
        for mode in access_modes:
            # we use assumption that read and write flags are mutually exclusive (same as in SystemBus logic in Renode, and comment in FieldMode.cs)
            try:
                if inverse:
                    return SystemRDLConverter.renode_onread.inverse[mode]
                else:
                    return SystemRDLConverter.renode_onread[mode]
            except KeyError:
                pass

        return ''

    def _convert_field(self, field: Field, reset_val: str = '') -> RDLField:
        if self.active_layout_id != -1 and (block_id := field.raw_data['BlockId']) != self.active_layout_id:
            raise RuntimeError(f'Omitting alternate configuration {block_id}, different than selected layout id {self.active_layout_id}')

        name  = field.raw_data['Name']
        start = field.Start
        end   = field.End

        modes = field.get_field_modes()
        sw_access_mode = self.get_sw_access(field, modes)
        sw_write_properties = self.get_sw_write_access_properties(modes)
        sw_read_properties = self.get_sw_read_access_properties(modes)
        desc = ['The field is a Tag.'] if field.is_any_special_kind('Tag') else []

        if field.is_any_special_kind('Ignored'):
            desc += ['This register is ignored.']

        # if is not accessible skip, peakrdl gets existential crisis if it finds non-accessible field
        if sw_access_mode == 'na':
            raise RuntimeError(f'Non-accessible field "{name}" will be skipped from SystemRDL model.')
        elif sw_access_mode == 'user':
            # Some non-standard access mode - possibly generated at runtime depending on some constructor value
            sw_access_mode = 'rw'
            sw_write_properties = 'wuser'
            sw_read_properties = 'ruser'
            desc += ['This field uses access mode that cannot be resolved at compile-time.']

        if cb_info := field.get_callback_info():
            desc += [f'This field has the following custom Renode callbacks: {", ".join(cb_info)}.']

        rdl_field = self.RDLField(start, end, name, sw_access_mode, 
                                  sw_write_properties, sw_read_properties,
                                  reset_val, desc, field.raw_data['BlockId'])
        
        return rdl_field

    def _convert_register(self, register_offsets: set, register: Register) -> RDLRegister:
        def _int_or_none(val, alt=None):
            try:
                return int(val)
            except TypeError:
                return alt

        fields = []
        field_names = set()
        desc = []
        is_external = False
        field_offsets = set()

        for f in register.Fields:
            field = Field(f)
            field_reset = None
            if (reset := _int_or_none(register.raw_data['ResetValue'])) is not None:
                field_reset = ( reset & ((1 << (int(field.End) + 1)) - 1) ) >> int(field.Start)
            else:
                # Unknown reset value
                field_reset = 0
            
            try:
                field_rdl = self._convert_field(field, field_reset)
                if field_rdl.name in field_names:
                    field_rdl.name = self._sanitize_name(field_rdl.name) + '_'
                    print(f'Correcting field name to {field_rdl.name} to avoid duplicates')
                else:
                    field_names.add(field_rdl.name)

                if field_rdl.start in field_offsets:
                    field_rdl.erroneus = f'Field "{field_rdl.name}" overlaps another field at the same offset ({hex(field_rdl.start)}) and will be skipped, as it is illegal in SystemRDL.'
                    self.header.append(field_rdl.erroneus)
                    print(field_rdl.erroneus)
                else:
                    field_offsets.add(field_rdl.start)

                if reset is None:
                    field_rdl.description += ['This field has reset value calculated at runtime.']

                if field_rdl.sw_onwrite == "wuser" or field_rdl.sw_onread == "ruser":
                    is_external = True

                fields += [field_rdl]
            except RuntimeError as e:
                print(e)

        if not fields:
            if not self.fill_empty_registers:
                raise RuntimeError(f'Register "{register.Name}" with no fields will be skipped, as it is illegal in SystemRDL.')
            else:
                fields += [self.RDLField(0, (register.get_width() or 8) - 1, 'DUMMY', sw_access='rw', hw_reset_val=register.raw_data['ResetValue'],\
                                         description=['This is a dummy r/w field, generated just to show the register in specs'])]

        cond = self.RDLConditionalBlock(omit_if_possible=True)
        for field in fields:
            cond.add_action_to_cond(f'VARIANT{field.block_id}', [field])
        fields = [cond]  # leverage Python here - cond will unwind semi-transparently

        if cb_info := register.get_callback_info():
            desc += [f'This register has the following custom Renode callbacks: {", ".join(cb_info)}']

        # SystemRDL would otherwise infer width of 32, which is not necessarily true. 
        # This especially will improve elaboration of older syntax peripherals, where we don't get field coverage either way
        # TODO: improve width detection in such peripherals
        width = register.get_width()
        if not width:
            width = 8
            desc += ["Could not determine register's width, guessed 8 bits."]

        erroneus=''

        if register.Offset in register_offsets:
            erroneus = f'Register "{register.Name}" overlaps another register at the same offset ({hex(register.Offset)}) and will be skipped, as it is illegal in SystemRDL.'
            self.header.append(erroneus)
            print(erroneus)
        else:
            register_offsets.add(register.Offset)

        rdl_reg = self.RDLRegister(register.Name, register.raw_data['OriginalName'], 
                                   width, register.Offset,
                                   fields, desc, needs_external=is_external, erroneus=erroneus)

        if not self.unwind_array and register.raw_data['ArrayInfo']['IsArray']:
            rdl_reg.stride = register.raw_data['ArrayInfo']['Stride']
            rdl_reg.length = register.raw_data['ArrayInfo']['Length']

        return rdl_reg

    def get_header(self) -> str:
        if len(self.header) == 0:
            return ""

        rets =  "/* Some conversion errors detected, this might mean that the peripheral's registers structure in this file is incomplete.\n"
        rets += " * Please refer to logs and comments in the file for more information.\n"
        rets += " * \n"

        for line in self.header:
            rets += " * " + line + "\n"

        return rets + " */\n\n"

    def convert_to(self, reg_group: RegistersGroup) -> str:
        rets =  f'addrmap {{'
        rets += '\n    desc = "Generated by RenodeModelsAnalyzer";'
        register_offsets = set()
        for reg in reg_group.Registers:
            if not self.unwind_array and reg.raw_data['ParentReg']:
                continue
            try:
                rets += '\n' + indent(str(self._convert_register(register_offsets, reg)), '    ')
            except RuntimeError as e:
                print(e)
        rets += f'\n}} {reg_group.GroupName}_addrmap;'
        return rets
    
    def convert_from(self, file_path: str) -> RegistersGroup:
        try:
            from systemrdl import RDLCompiler, RDLWalker, RDLListener, WalkerAction
        except ImportError:
            raise RuntimeError('You have to install SystemRDL compiler to use this feature. ' \
                   'Visit https://systemrdl-compiler.readthedocs.io/en/stable/index.html for further instructions.')

        rdlc = RDLCompiler()
        rdlc.compile_file(file_path)
        root = rdlc.elaborate()

        from systemrdl.node import FieldNode, RegfileNode, RegNode, AddrmapNode, AddressableNode

        class RDLToRenodeJsonConverter(RDLListener):
            def __init__(self, converter: SystemRDLConverter):
                self.converter = converter

                # persistent helpers - they are reset when walker exits from specific components
                self.rg = None          # main register group

                self.holding_reg = None # current register
                self.holding_id = 0     # current id counter for each field in a register
                self.agg_reset = 0      # aggregated reset value over all fields in a register

                self.register_name_prefix = ''  # prefix for a register name when inside a logical grouping
                self.array_parent = [None]        # first element of an array of components
                self.array_ctr = [0]

            def _begin_array(self, node: AddressableNode, parent: str):
                if node.is_array and node.current_idx[0] == 0:
                    # I'm the first element in the array of elements
                    # all other elements will have me referenced as ParentReg
                    if len(node.array_dimensions) > 1:
                        raise RuntimeError('Multidimensional arrays are unsupported')

                    self.array_parent.append(parent)
                    self.array_ctr.append(node.array_dimensions[0])

            def _end_array(self):
                # reset first array element
                # they work as stacks, to support nested arrays
                if self.array_ctr[-1] == 0:
                    self.array_ctr.pop()
                    if not self.array_ctr:
                        self.array_ctr = [0]

                    self.array_parent.pop()
                    if not self.array_parent:
                        self.array_parent = [None]

            def _get_node_name(self, node: AddressableNode):
                name = ''
                if self.register_name_prefix:
                    name = f'{{{self.register_name_prefix}}}_'
                name += node.get_path_segment() 
                return name

            def enter_Addrmap(self, node: AddrmapNode):
                if not any(filter(lambda a: isinstance(a, RegfileNode) or isinstance(a, RegNode), node.children())):
                    return

                if not self.rg:
                    self.rg = RegistersGroup([], group_name=node.get_path_segment())
                else:
                    raise RuntimeError('Many addrmaps are not supported')

            def enter_Regfile(self, node: RegfileNode):
                # A Regfile is a grouping of several Registers or Regfiles - we don't have a similar structure in Renode
                # To emphasize a logical grouping, we will just add a prefix to fields inside the regfile
                self.register_name_prefix += node.get_path_segment()

                #for child in node.children():
                #    self._begin_array(node, self._get_node_name(child))

            def exit_Regfile(self, node: RegfileNode):
                self.register_name_prefix = ''

                #self._end_array()

            def enter_Reg(self, node: RegNode):
                self.array_ctr[-1] -= 1
                
                name = self._get_node_name(node)

                reg = Register({
                    'Name':         name,
                    'Description':  node.get_property('desc'),
                    'Address':      node.absolute_address,
                    'Width':        node.get_property('regwidth'),
                    'ResetValue':   'This would fail',
                    'ParentReg':    self.array_parent[-1],
                    'SpecialKind':  '',
                    'CallbackInfo': dict(),
                    'Fields':       [],
                })
                self.rg.raw_data['Registers'].append(reg)
                self.holding_reg = reg

                self._begin_array(node, name)

            def enter_Field(self, node: FieldNode):
                self.holding_id += 1

                field_modes = []

                def _access_prop_transform(prop):
                    return str(prop).split('.')[1]
                if prop := node.get_property('onread'):
                    field_modes += [SystemRDLConverter.get_sw_read_access_properties([_access_prop_transform(prop)], True)]
                if prop := node.get_property('onwrite'):
                    field_modes += [SystemRDLConverter.get_sw_write_access_properties([_access_prop_transform(prop)], True)]

                if node.is_sw_readable:
                    field_modes += ['Read']

                if node.is_sw_writable:
                    field_modes += ['Write']

                if not field_modes:
                    print('No access modes translated for field, fixup the converter.')

                f = Field({
                    'UniqueId':         self.holding_id,
                    'Name':             node.get_property('name'),
                    'Description':      node.get_property('desc'),
                    'Range': {
                        'Start':        node.lsb, # TODO: are these always legal?
                        'End':          node.msb,
                    },
                    'BlockId':          0,
                    'GeneratorName':    None,
                    'SpecialKind':      '',
                    'CallbackInfo':     dict(),
                    'FieldMode':        ' | '.join(field_modes),
                })

                reset = node.get_property('reset') or 0
                reset = reset << node.lsb
                self.agg_reset |= reset

                self.holding_reg.raw_data['Fields'].append(f)

            def exit_Reg(self, node: RegNode):
                self.holding_reg.raw_data['ResetValue'] = self.agg_reset

                self.holding_reg = None
                self.holding_id = 0
                self.agg_reset = 0

                self._end_array()

        walker = RDLWalker(unroll=True)
        listener = RDLToRenodeJsonConverter(self)
        walker.walk(root, listener)

        return listener.rg