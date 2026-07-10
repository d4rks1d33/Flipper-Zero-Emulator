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
import RenodeModelsCompare.regcompare as Regcompare

class TestRegcompare:
    simple_wc = Regcompare.WordCompare().register_comparator(Regcompare.MatchSimple())

    def test_match_type_creation(self):
        with pytest.raises(TypeError):
            Regcompare.MatchType('aaaa', '', '')

        assert Regcompare.MatchType(True, '', '').percent == 100
        assert Regcompare.MatchType(40, '', '').percent == 40
        assert Regcompare.MatchType(False, '', '').percent == 0
        assert Regcompare.MatchType(0.21, '', '').percent == 21
        
        
        assert Regcompare.MatchType(0.21, '', '') > Regcompare.MatchType(11, '', '')

    def test_word_compare_exact(self):
        _, match =  list(self.simple_wc.run_compare('DevTxUpper', 'DeviceTransmitUpper', False))[0]
        assert match == Regcompare.MatchType.Exact('', '')
        
        _, match =  list(self.simple_wc.run_compare('DevTxUpperPrev', 'DeviceTransmitUpper', False))[0]
        assert match == Regcompare.MatchType(75, '', '')
        assert self.simple_wc.unmatched == []
