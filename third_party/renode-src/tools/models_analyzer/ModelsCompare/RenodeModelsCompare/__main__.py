#
# Copyright (c) 2022-2025 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import sys
import json, argparse, os
import pathlib
from typing import Any, Iterator
from RenodeModelsCompare.registers import register, systemrdl_converter
from RenodeModelsCompare import report_generator, regcompare, models_match

def create_parsers():
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(title='subcommands', dest='verb')

    summary_parser = subparsers.add_parser('summary', help='Print summary of peripheral from JSON/SVD data')
    summary_parser.add_argument("files", nargs='+', help='Files/directories to parse')
    summary_parser.add_argument('--html', nargs='?', required=False, const='report.html', help="Output summary in html format")
    summary_parser.add_argument('--include-diagnostics', action='store_true', required=False, default=False)

    compare_parser = subparsers.add_parser('compare', help='Compare peripheral models')
    compare_parser.add_argument('files', nargs='+', help='A pair of files to compare')

    misc_parser = subparsers.add_parser('misc', help='Misc helper/debugging utils')
    misc_parser.add_argument('target')
    misc_parser.add_argument('--svd-list-peripherals', action='store_true', required=False)
    misc_parser.add_argument('--is-svd-file', action='store_true', required=False)
    misc_parser.add_argument('--validate-rdl', action='store_true', required=False, help='Use SystemRDL compiler to validate RDL correctness')

    convert_parser = subparsers.add_parser('convert', help='Convert between file formats')
    convert_parser.add_argument('target')
    convert_parser.add_argument('--from-svd', required=False, help='Transcribe svd peripheral to our JSON format.')
    convert_parser.add_argument('--to-systemrdl', required=False, help='Transcribe our peripheral format to SystemRDL.')
    convert_parser.add_argument('--from-systemrdl', required=False, help='Transcribe SystemRDL to our peripheral format.')
    
    convert_parser.add_argument('--layout', required=False, default=-1, help='Select layout id.')
    convert_parser.add_argument('--fill-empty-registers', action='store_true', required=False, help='Fill empty registers with dummy rw field')
    convert_parser.add_argument('--unwind-register-array', action='store_true', required=False, help='Unwind arrays (DefineMany) of registers')
    convert_parser.add_argument('--compact-groups', action='store_true', required=False, help='Store many RegisterGroups in a single file')

    return parser

######

def utils(args):
    if args.is_svd_file:
        print('Is SVD:', register.probe_is_file_svd(args.target))
    if args.svd_list_peripherals:
        print('Found peripherals:', [*register.svd_get_peripheral_names(args.target)])
    if args.validate_rdl:
        if not systemrdl_converter.validate_rdl(args.target):
            sys.exit(1)


def convert(args):
    if args.from_svd:
        if ':' in args.from_svd:
            (file_path, peripheral) = args.from_svd.split(':')

            register.RegistersGroup.from_svd_file(file_path, peripheral).to_json_file(args.target)
        else:
            print('For SVDs the syntax is: path:peripheral:', args.target)

    if args.to_systemrdl:
        if not os.path.isdir(args.target):
            rgs = register.RegistersGroup.from_json_file(args.target)
        else:
            rgs = register.RegistersGroup.from_json_file(get_subfolder_file(args.target, 'registersInfo', os.path.basename(args.target)))

        if len(rgs) > 1:
            if not args.compact_groups:
                print('Found several register groups, each will be emitted into separate file')
            else:
                print('Found several register groups, but they will be compacted')

        opts = {'layout_id': args.layout, 'fill_empty_registers': args.fill_empty_registers, \
                'unwind_array': args.unwind_register_array}

        if not args.compact_groups:
            for group in rgs:
                path = pathlib.Path(args.to_systemrdl)
                path = path.parent / (path.stem + '-' + str(group.GroupName) + path.suffix if len(rgs) > 1 else path.name)
                print(f'Saving part to {path}')
                group.to_systemrdl_file(path, **opts)
        else:
            from RenodeModelsCompare.registers.systemrdl_converter import SystemRDLConverter
            converter = SystemRDLConverter(**opts)

            path = pathlib.Path(args.to_systemrdl)
            path = path.parent / path.name
            print(f'Saving to {path}')
            with open(path, 'w') as file:
                # TODO: this top-level map is generated in two places, unify it
                file.write(f'addrmap {rgs[0].PeripheralName} {{\n\n')

                content = ""

                for group in rgs:
                    content += converter.convert_to(group)
                    content += "\n"

                file.write(converter.get_header())
                file.write(content)
                file.write('\n\n};')
    
    if args.from_systemrdl:
            rg = register.RegistersGroup.from_systemrdl_file(args.from_systemrdl)
            rg.to_json_file(args.target)

######

def get_subfolder_file(path: str, suffix: str, name: str) -> str:
    # remember to remove extensions from name
    return os.path.join(path, f'{name}-{suffix}.json')

def select_report_gen(args) -> 'report_generator.ReportGenerator':
    return report_generator.ReportGenerator() if not args.html else report_generator.HtmlReportGenerator(args.html)

def load_files_for_report(args, file_path: str, report_gen: 'report_generator.ReportGenerator') -> None:
    if os.path.isdir(file_path):
        # structured jsons from analyzers
        name = os.path.basename(file_path)

        if args.include_diagnostics:
            report_gen.diagnostics = []
            if os.path.exists(diag_info := get_subfolder_file(file_path, 'diagnosticInfo', name)):
                with open(diag_info) as filep:
                    report_gen.diagnostics = json.load(filep)

        if os.path.exists(reg_info := get_subfolder_file(file_path, 'registersInfo', name)):
            report_gen.load_reg_info(os.path.splitext(name)[0], register.RegistersGroup.from_json_file(reg_info)).generate_report()
    else:
        # standalone file
        if register.probe_is_file_svd(file_path):
            if ':' in file_path:
                (file_path, peripherals) = file_path.split(':')
            else:
                print('Skipping! For SVDs the syntax is: path:names,of,peripherals,to,parse:', file_path)
                return

            if ',' in peripherals:
                peripherals = peripherals.split(',')
            else:
                peripherals = [peripherals]
            
            # standalone svd file
            for peripheral in peripherals:
                report_gen.load_reg_info(peripheral, register.RegistersGroup.from_svd_file(file_path, peripheral)).generate_report()
        else:
            # standalone json file
            report_gen.load_reg_info(report_generator.ReportGenerator.file_to_peripheral_name(file_path), register.RegistersGroup.from_json_file(file_path)).generate_report()

def summarize(args):
    generator = select_report_gen(args)
    with generator as report_gen:
        for file_path in args.files:
            try:
                load_files_for_report(args, file_path, report_gen)
            except json.decoder.JSONDecodeError as error:
                print(f'Exception while parsing file "{file_path}" the file will be omitted:', error)

######

def file_to_reg_group(file_path: str) -> 'register.RegistersGroup|None':
    if os.path.isdir(file_path):
        name = os.path.basename(file_path)

        if os.path.exists(reg_info := get_subfolder_file(file_path, 'registersInfo', name)):
            rg = register.RegistersGroup.from_json_file(reg_info)
            if len(rg) > 1:
                print('More than one RegisterGroup in file, selecting first')
            return rg[0]
    else:
        if register.probe_is_file_svd(file_path):
            if ':' in file_path:
                (file_path, peripheral) = file_path.split(':')
            else:
                print('For SVDs the syntax is: path:peripheral:', file_path)
                return None
            return register.RegistersGroup.from_svd_file(file_path, peripheral)
        else:
            rg = register.RegistersGroup.from_json_file(file_path)
            if len(rg) > 1:
                print('More than one RegisterGroup in file, selecting first')
            return rg[0]

def compare(args):
    if len(args.files) < 2:
        print('Not enough files')
        return

    file1 = args.files[0]
    files = args.files[1:]

    regs1 = file_to_reg_group(file1)

    results = []
    for file in files:
        regs2 = file_to_reg_group(file)
        if not regs2:
            continue

        print('###########')
        print('Comparing', file1, 'with', file)

        cmpr = regcompare.RegCompare(regs1, regs2)
        result = cmpr.compare_register_group_layout()

        results += [result]

    models_match.match_most_similar(results)

######

def main():
    parser = create_parsers()
    args = parser.parse_args()

    verbs = {
        'summary': summarize,
        'compare': compare,
        'misc':    utils,
        'convert': convert,
    }

    if args.verb not in verbs:
        parser.print_usage()
        return 1

    verbs[args.verb](args)

if __name__ == "__main__":
    main()