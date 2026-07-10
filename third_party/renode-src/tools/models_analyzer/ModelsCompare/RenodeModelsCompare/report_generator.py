#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import os
import re
from datetime import datetime
from typing import Iterator, List
from RenodeModelsCompare.registers.register import Register, RegistersGroup, Field

# disable escaping html tags in PrettyTable
import html
html.escape = lambda *args, **kwargs: args[0]
from prettytable import PrettyTable

class ReportGenerator:
    __slots__ = ['peripheral_name', 'reg_json', 'diagnostics']

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        pass

    @staticmethod
    def _hex_or_none(e: int) -> 'str|None':
        return hex(e) if e is not None else e
    
    @staticmethod
    def file_to_peripheral_name(filename: str) -> str:
        return re.split('(\.cs-[a-zA-Z]+[iI]nfo\.json)', os.path.basename(filename), maxsplit=1)[0]

    def load_reg_info(self, peripheral_name: str, reg_group: List[RegistersGroup]) -> 'ReportGenerator':
        self.peripheral_name = peripheral_name
        self.reg_json = reg_group
        return self

    def generate_report(self) -> None:
        for group in self.reg_json:
            print(f'#### {group.GroupName} ###')
            regs_table = self.get_register_table(group.Registers, group.GroupName, self.peripheral_name)
            print(regs_table)
            for register in group:
                # omit field table generation for identical registers
                if register['ParentReg']:
                    continue

                if fields := register['Fields']:
                    print(register["Name"])
                    fields_table = self.get_register_fields_table(fields, register, group.GroupName, self.peripheral_name, register['Name'])
                    print(fields_table)

    def _add_register_row(self, register: Register, group_name: str, peripheral_name: str, regsTable: PrettyTable) -> None:
        reg_name = register["Name"]
        if register["ParentReg"]:
            reg_name += ' [M]'

        regsTable.add_row([
            hex(register["Address"]), 
            reg_name, 
            register["Width"] or "???", 
            self._hex_or_none(register["ResetValue"]) or "???",
            register["SpecialKind"],
            ', '.join(register.get_callback_info())
        ])

    def _add_fields_row(self, field: Field, reg_json: Register, group_name: str, peripheral_name: str, register_name: str, fieldsTable: PrettyTable) -> None:
        fieldsTable.add_row([
            field['UniqueId'],
            field["Name"] or "[No name]",
            f'{field["Range"]["Start"]} .. {field["Range"]["End"]} ({field.get_width() or "?"})',
            field.get_field_modes_str(),
            field["SpecialKind"],
            field["GeneratorName"],
            ', '.join(field.get_callback_info())
        ])

    def get_register_table(self, reg_json: List[Register], group_name: str, peripheral_name: str) -> PrettyTable:
        regsTable = PrettyTable()
        regsTable.field_names = ['Offset', 'Name', 'Width', 'Reset Value', 'Advanced info', 'Callbacks']
        for register in sorted(reg_json, key=lambda reg: reg["Address"]):
            self._add_register_row(register, group_name, peripheral_name, regsTable)
        return regsTable
    
    def get_register_fields_table(self, fields_json_fragment: Field, reg_json: Register, group_name: str, peripheral_name: str, register_name: str) -> PrettyTable:
        fieldsTable = PrettyTable()
        fieldsTable.field_names = ['Id', 'Name', 'Bits', 'Access Mode', 'Advanced Info', 'Renode Generator', 'Callbacks']

        for field in sorted(fields_json_fragment, key=lambda fl: (fl["BlockId"], fl["Range"]["Start"])):
            self._add_fields_row(field, reg_json, group_name, peripheral_name, register_name, fieldsTable)

        return fieldsTable

class HtmlReportGenerator(ReportGenerator):
    __slots__ = ['file_name', '_file', 'peripheral_names']

    def __init__(self, output_file_name: str = None) -> None:
        self.file_name = output_file_name or f'report-{datetime.now().strftime("%d%m%Y-%H%M%S")}.html'
        self.peripheral_names = []
        self.diagnostics = []

    def __enter__(self):
        self._file = open(self.file_name, 'w')

        self.write_css_style()
        self._file.write(f'<a href="#peripheral-index"> Peripheral list </a>')
        
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.generate_register_index()
        self._file.close()

    @staticmethod
    def _get_register_fields_name(peripheral_name: str, group_name: str, register_name: str) -> str:
        return f'{peripheral_name}-fields-{group_name}-{register_name}'

    def _add_register_row(self, register: Register, group_name: str, peripheral_name: str, regsTable: PrettyTable) -> None:
        reg_name = f'<span id="{peripheral_name}-{group_name}-{register["Name"]}">' + register.Name + '</span>'

        if parent := register["ParentReg"]:
            reg_name += f' [See: {parent}]'
            reg_name = f'<a href="#{peripheral_name}-{group_name}-{parent}">' + reg_name + '</a>'
        elif register["Fields"]:
            reg_name = f'<a href="#{self._get_register_fields_name(peripheral_name, group_name, register.Name)}">' + reg_name + '</a>'
        
        regsTable.add_row([
            hex(register["Address"]),
            reg_name,
            register["Width"] or "???",
            self._hex_or_none(register["ResetValue"]) or "???",
            register["SpecialKind"],
            ', '.join(register.get_callback_info())
        ])

    def _add_fields_row(self, field: Field, reg_json: Register, group_name: str, peripheral_name: str, register_name: str, fieldsTable: PrettyTable) -> None:
        name = field["Name"] or "[No name]"

        name = f'<span id={self._get_register_fields_name(peripheral_name, group_name, register_name)}-{field["UniqueId"]}>' + name + '</span>'

        fieldsTable.add_row([
            field['UniqueId'],
            name,
            f'{field["Range"]["Start"]} .. {field["Range"]["End"]} ({field.get_width() or "?"})',
            field.get_field_modes_str(),
            field["SpecialKind"],
            field["GeneratorName"],
            ', '.join(field.get_callback_info())
        ])

    @staticmethod
    def _count_tags(fields: Iterator[Field]) -> int:
        return sum([1 for f in fields if f.is_any_special_kind('Tag')])

    def print_additional_stats(self, reg_json: RegistersGroup) -> None:
        self._file.write('<div class="registerGroup-short-stats">')

        if total_fields := sum([len(register['Fields']) for register in reg_json]):
            total_tags = sum([self._count_tags(register['Fields']) for register in reg_json])
            self._file.write(f'Total Tags/Fields ratio: [{total_tags}/{total_fields}] ({(total_tags/total_fields):.0%}) <br />')

        if total_regs := len(reg_json):
            maybeUdefined_regs = sum([1 for register in reg_json if register.is_any_special_kind('MaybeUndefined')])
            self._file.write(f'MaybeUndefined/Total ratio: [{maybeUdefined_regs}/{total_regs}] ({(maybeUdefined_regs/total_regs):.0%})')

        self._file.write('</div>')

    def print_diagnostics(self) -> None:
        if not self.diagnostics:
            return

        map_severity_to_val = {
            'Fatal': 0,
            'Error': 1,
            'Warning': 2,
            'Info': 3,
            'Hidden': 4,
        }
        self.diagnostics.sort(key=lambda k: map_severity_to_val[k['Severity']])

        def glue_diagnostic_string(diag: dict) -> str:
            starting_line = diag['LocationSpan']['Span']['_start']['_line']
            starting_col  = diag['LocationSpan']['Span']['_start']['_character']
            severity      = diag['Severity']

            return f"[{severity}] {diag['Id']} (Ln {starting_line}, Col {starting_col}): {diag['HumanMessage']}"

        self._file.write(f'''
            <div class="peripheral-diagnostics">
            {
                "<br />".join([glue_diagnostic_string(d) for d in self.diagnostics])
            }
            </div>
        ''')

    def write_css_style(self) -> None:
        self._file.write('''
            <style>
                table {
                    border: 1px solid black;
                    text-align: center;
                }
                td {
                    padding-left: 1em;
                    padding-right: 1em;
                    text-align: center;
                    vertical-align: top;
                }

                .register-fields-table td {
                    border: 1px solid black;
                }
            </style>
        ''')

    def generate_report(self) -> None:
        self._file.write(f'<h2 id="{self.peripheral_name}">{self.peripheral_name}</h2>')
        self.peripheral_names.append(self.peripheral_name)
        
        self.print_diagnostics()
        for group in self.reg_json:
            self.print_additional_stats(group)

            table = self.get_register_table(group, group.GroupName, self.peripheral_name)
            html = table.get_html_string(attributes={'id': f'{self.peripheral_name}-{group.GroupName}-registers', 'class': 'register-table-style', 'width': '80%'})
            self._file.write(html)

        for group in self.reg_json:
            for register in sorted(group, key=lambda reg: reg["Address"]):
                # omit field table generation for identical registers
                if register['ParentReg']:
                    continue

                if fields := register["Fields"]:
                    self._file.write(f'<h3 id="{self._get_register_fields_name(self.peripheral_name, group.GroupName, register["Name"])}">{register["Name"]}</h3>')
                    self._file.write(f'<p> Tags: [{self._count_tags(fields)} tags/{len(fields)} fields] </p>')

                    self.write_register_bits_table(self.peripheral_name, register["Width"], group.GroupName, register)

                    table = self.get_register_fields_table(fields, register, group.GroupName, self.peripheral_name, register['Name'])
                    html = table.get_html_string(attributes={'id': self.peripheral_name + f'-{group.GroupName}-fields', 'class': 'fields-table-style', 'width': '80%'})
                    self._file.write(html)

    def write_reset_bits_row(self, reset_value: int, reg_width: int):
        if not reg_width or reg_width <= 0 or not reset_value or reset_value == 0:
            return

        self._file.write(f'''
            <tr>
                <td colspan="2">
                    Reset bits
                </td>
        ''')

        bits = format(reset_value, 'b').zfill(reg_width)
        for bit in bits[0:reg_width]:
            self._file.write(f'''
                <td style="all: revert">
                    {bit}
                </td>
            ''')

        self._file.write('</tr>')

    # this won't be displayed in console mode - the table is painstakingly constructed for html report
    def write_register_bits_table(self, peripheral_name: str, width: int, group_name: str, register_json: Register) -> None:
        if width <= 0:
            raise ValueError("Invalid width")

        fields = sorted(register_json["Fields"], key=lambda a: (-a["BlockId"], a["Range"]["End"]), reverse=True)

        self._file.write(f'''
        <table class="register-fields-table" id="{peripheral_name}-{group_name}-{register_json["Name"]}-bits-table">
            <thead>
                <tr>
                    <th>
                        Offset
                    </th>
                    <th>
                        Register
                    </th>
        ''')
        
        for i in reversed(range(0, width)):
            self._file.write(f'<th> {i} </th>')

        self._file.write(f'''
                <th>
                    Layout variant
                </th>
                </tr>
            </thead>
            <tbody>
                <tr>

                    <td>
                        {hex(register_json.Offset)}
                    </td>

                    <td>
                        {register_json.Name}
                    </td>
        ''')

        position = width - 1
        blockId = fields[0]["BlockId"]

        def fill_line_end_gap(position):
            return f'<td colspan="{position + 1}" > [???] </td>' if position >= 0 else ''

        def layout_id(block_id):
            return f'<td> {block_id} </td>'

        for field in fields:
            def next_line():
                nonlocal position, blockId
                self._file.write(f'''
                        {fill_line_end_gap(position)}
                        {layout_id(blockId)}
                    </tr>
                    <tr>
                        <td colspan="2"> alternative layout </td>
                ''')
                blockId = field['BlockId']
                position = width - 1

            field_len = field['Range']['End'] - field['Range']['Start'] + 1
            if position < 0 or blockId != field["BlockId"]:
                # new round - there are overlapping registers - possibly because of some conditional generation
                # or we are in a new code block
                next_line()
            
            while True:
                if position < 0:
                    raise Exception('Position less than 0. Something is wrong with input data')
                if field['Range']['End'] > width:
                    raise ValueError(f'End {field["Range"]["End"]} farther than width {width}')
                elif field['Range']['End'] == position:
                    self._file.write(f'''
                        <td colspan={field_len}>
                            <a href="#{self._get_register_fields_name(peripheral_name, group_name, register_json['Name'])}-{field['UniqueId']}"> {field['UniqueId']} </a>
                        </td>
                    ''')
                    position -= field_len
                    break
                elif field['Range']['End'] > position:
                    # got to next line
                    next_line()
                else:
                    gap_len = position - field['Range']['End']
                    self._file.write(f'''
                        <td colspan={gap_len}>
                            [???]
                        </td>
                    ''')
                    position -= gap_len
        
        self._file.write(fill_line_end_gap(position))
        self._file.write(layout_id(blockId))

        self._file.write('</tr>')
        
        self.write_reset_bits_row(register_json['ResetValue'], register_json['Width'])
        
        self._file.write('''
                </tbody>
            </table>
            <br />
        ''')

    def generate_register_index(self) -> None:
        self._file.write('<h2 id="peripheral-index">Available peripherals:</h2>')
        self._file.write('<div>')

        for name in self.peripheral_names:
            link = f'<span> &nbsp <a href="#{name}">{name}</a> &nbsp </span>'
            self._file.write(link)

        self._file.write('</div>')
        self._file.write(f'<div> Total: {len(self.peripheral_names)} </div>')
