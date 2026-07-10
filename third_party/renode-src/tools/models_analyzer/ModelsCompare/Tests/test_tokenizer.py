#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import sys
import os
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

import pytest
from RenodeModelsCompare.tokenizer import *

class TestTokenizer:
    tkz = get_default_tokenizer_pipeline()

    def _repr(self, items: Iterator):
        return ', '.join(repr(item) for item in items)

    def _assert_tokens_equal(self, a: str, b: str):
        assert self._repr([*self.tkz(a)]) == b

    def test_token_creation(self):
        Token('123', True)
        Token('123', False)
        with pytest.raises(TypeError):
            Token(123, True)

    def test_token_str(self):
        assert str(Token('123', True)) == '123'

    def test_token_repr(self):
        assert repr(Token('123', True)) == '<123, a>'
        assert repr(Token('123', False)) == '<123, n>'

    def test_default_tokenizer_tests(self):
        self._assert_tokens_equal('_ABRegTransmitRxHi_Th_aa', '<_, n>, <AB, n>, <Reg, n>, <Transmit, n>, <Rx, n>, <Hi, n>, <_, n>, <Th, n>, <_, n>, <aa, n>')
        self._assert_tokens_equal('DriverClockingMMCMWrite', '<Driver, n>, <Clocking, n>, <MMCM, n>, <Write, n>')
        self._assert_tokens_equal('IRQTxEnableDev', '<IRQ, a>, <Tx, n>, <Enable, n>, <Dev, n>')

    def test_name_tokenizer_creates_atomic_token(self):
        self._assert_tokens_equal('GPIO0Pin', '<GPIO, a>, <0, n>, <Pin, n>')
        self._assert_tokens_equal('I2Cn_IRQCTRL', '<I2C, a>, <n, n>, <_, n>, <IRQ, a>, <CTRL, n>')

    def test_name_tokenizer_order_of_names(self):
        self._assert_tokens_equal('LegacyPCIExpressEndpoint', '<Legacy, n>, <PCIExpress, a>, <Endpoint, n>')
