#
# Copyright (c) 2023-2025 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import abc
from RenodeModelsCompare.registers.register import RegistersGroup

class BaseConverter(abc.ABC):
    def get_header(self) -> any:
        return ""

    def convert_to(self, reg_group: RegistersGroup) -> any:
        raise NotImplementedError('Conversion is not implemented')

    def convert_from(self, external_repr: any) -> RegistersGroup:
        raise NotImplementedError('Conversion is not implemented')
