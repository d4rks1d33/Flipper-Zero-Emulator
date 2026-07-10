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
import RenodeModelsCompare.tokenizer as Tokenizer
import RenodeModelsCompare.synonyms as Synonyms

class TestNamesCompare:
    tkz = Tokenizer.get_default_tokenizer_pipeline()
    syn = Synonyms.SynonymsGen()
    synDel = Synonyms.SynonymsGen(gen_without_vowels=True)
    wg = Synonyms.WordGen()

    def test_synonyms_generation(self):
        assert [*self.syn.get_word_synonyms(str('Tx'))] == ['Tx', 'Transmit', 'Xmit', 'Transmitting']

    def test_synonyms_generation_without_vowels(self):
        assert [*self.synDel.get_word_synonyms(str('Txe'))] == ['Txe', 'Tx', 'Transmit', 'Trnsmt', 'Xmit', 'Xmt', 'Transmitting', 'Trnsmttng']
        assert [*self.synDel.get_word_synonyms(str('Iden'))] == ['Iden', 'dn', 'Identifier', 'dntfr', 'Id', 'd', 'Ident', 'dnt']

    def test_synonyms_compare_words(self):
        assert self.syn.compare_words('Tx', 'Transmit') == True
        assert self.syn.compare_words('Tx', 'Trnsmit') == False

    def test_synonyms_compare_words_without_vowels(self):
        assert self.synDel.compare_words('Txi', 'Trnsmit') == True
        assert self.synDel.compare_words('Txi', 'Trnsmitt') == False

    def test_word_gen(self):
        self.wg.add_tokens([*self.syn.get_word_synonyms('Tx')], [*self.syn.get_word_synonyms('Dev')], [*self.syn.get_word_synonyms('Offset')])
        assert self.wg.gen_possible_words_concat() == [
            'TxDevOffset',
            'TransmitDevOffset',
            'XmitDevOffset',
            'TransmittingDevOffset',
            'TxDeviceOffset',
            'TransmitDeviceOffset',
            'XmitDeviceOffset',
            'TransmittingDeviceOffset',
            'TxDevOff',
            'TransmitDevOff',
            'XmitDevOff',
            'TransmittingDevOff',
            'TxDeviceOff',
            'TransmitDeviceOff',
            'XmitDeviceOff',
            'TransmittingDeviceOff',
        ]
