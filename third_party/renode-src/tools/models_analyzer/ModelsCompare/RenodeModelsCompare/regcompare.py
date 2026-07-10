#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
from collections import namedtuple
import itertools
from abc import ABC
from functools import total_ordering
from math import ceil
import string
from typing import Any, Iterator, List, Tuple
from rapidfuzz.distance import Levenshtein

from RenodeModelsCompare.synonyms import SynonymsGen, WordGen
from RenodeModelsCompare.tokenizer import TokenizerPipeline, Token, get_default_tokenizer_pipeline, NameTokenizer

from RenodeModelsCompare.registers.register import RegistersGroup, Register

@total_ordering
class MatchType:
    __slots__ = ['percent', 'cutoff', 'word_a', 'word_b', 'fuzzy_match', 'comp_name']

    def __init__(self, percent: 'int|float|bool', word_a: str, word_b: str, fuzzy_match: bool = False) -> None:
        if isinstance(percent, float):
            percent = ceil(percent * 100)
        elif isinstance(percent, bool):
            percent = 0 if percent == False else 100
        elif not isinstance(percent, int):
            raise TypeError('Needs to be either bool, int or float')

        if percent < 0 or percent > 100:
            raise ValueError('Needs to be between 0 and 100')
        self.percent = percent

        # note that these don't matter in ordering
        self.cutoff = 0
        self.word_a = word_a
        self.word_b = word_b
        self.fuzzy_match = fuzzy_match
        self.comp_name = ''

    def __str__(self) -> str:
        return f'Similarity (0 - 100): {self.percent}%'

    def __repr__(self) -> str:
        return f'<{type(self).__name__} {"[FZ]" if self.fuzzy_match else ""} ["{self.word_a}":"{self.word_b}"] {self.percent}>'
    
    def __eq__(self, other) -> bool:
        if not isinstance(other, type(self)):
            raise TypeError
        return self.percent == other.percent

    def __lt__(self, other) -> bool:
        if not isinstance(other, type(self)):
            raise TypeError
        return self.percent < other.percent
    
    def __bool__(self) -> bool:
        return self.percent > self.cutoff

    @property
    def match(self) -> float:
        return self.percent

    @classmethod
    def Exact(cls, word_a: str, word_b: str, is_fuzzy: bool = False) -> 'MatchType':
        return cls(100, word_a, word_b, is_fuzzy)

    @classmethod
    def NoMatch(cls, word_a: str, word_b: str) -> 'MatchType':
        return cls(0, word_a, word_b, False)

class WordCompare:
    def __init__(self, *, synonyms_gen: 'SynonymsGen|None' = None, tokenizer_pipeline: 'TokenizerPipeline|None' = None, names: List[str] = []) -> None:
        # destroy zero-length or None specimens
        self.names = list(filter(lambda a: a, names))

        self.synonyms_gen = synonyms_gen if synonyms_gen != None else SynonymsGen()
        # extract tokens that might be the name of the peripheral - to do so add a new NameTokenizer at the beginning of the pipeline
        self.tokenizer = tokenizer_pipeline if tokenizer_pipeline else get_default_tokenizer_pipeline().prepend_tokenizer(NameTokenizer(self.names))
        
        self.comparators = []
        self.fuzzy_comparators = []

        # TODO: extract these to a helper struct. It's becoming unreadable
        self.matches = {}
        self.best_matches = {}
        self.unmatched = []
        self.runs = 0

    def _reset(self) -> 'WordCompare':
        for comp in self.comparators:
            comp.reset()
        return self

    def register_comparator(self, comp: 'Comparator', fuzzy: bool = False) -> 'WordCompare':
        if comp.is_fuzzy or fuzzy:
            self.fuzzy_comparators.append(comp)
        else:
            self.comparators.append(comp)
        
        self.matches[comp.name] = []
        return self

    def run_compare(self, word_a: str, word_b: str, fallback_fuzzy: bool, *args, **kwargs) -> 'Tuple[str, MatchType]':
        self.runs += 1
        match_best = MatchType.NoMatch(word_a, word_b)

        def run_cmp(comparators):
            nonlocal match_best
            for comp in comparators:
                comp.reset().inject(self.synonyms_gen, self.tokenizer, self.names)

                name = comp.name
                match = comp.match(word_a, word_b, *args, **kwargs)
                match.comp_name = name

                match_best = max(match_best, match)

                self.matches[name].append(match)

                # in case of a tie, max returns first element - this is important so the first comparator results are prioritized
                self.best_matches[(word_a, word_b)] = max(self.best_matches.get((word_a, word_b), MatchType.NoMatch(word_a, word_b)), match) 

                yield (name, match)
                if match == MatchType.Exact(word_a, word_b):
                    raise GeneratorExit

        try:
            yield from run_cmp(self.comparators)

            if fallback_fuzzy:
                print('[FZ] Trying fuzzy logic to get better match!')
                yield from run_cmp(self.fuzzy_comparators)
        
        # already found 100% match
        except GeneratorExit:
            return
        
        if not match_best > MatchType.NoMatch(word_a, word_b):
            self.unmatched += [[word_a, word_b]]
            # no match found at all, so no best match
            del self.best_matches[(word_a, word_b)]

    def get_unmatched(self) -> set:
        return self.unmatched

class Comparator(ABC):
    __slots__ = ['tokenizer', 'synonyms_gen', 'names', 'is_fuzzy']

    @property
    def name(self) -> str:
        return type(self).__name__

    def inject(self, synonyms_gen: SynonymsGen, tokenizer_pipeline: TokenizerPipeline, names: List[str]) -> None:
        self.tokenizer = tokenizer_pipeline
        self.synonyms_gen = synonyms_gen
        self.names = names

    def reset(self) -> 'Comparator':
        return self

    def compare(self, word_a: str, word_b: str, *args, **kwargs) -> MatchType:
        pass

class MatchSimple(Comparator):
    __slots__ = ['matched_exact', 'tokens_parsed']

    is_fuzzy = False

    def _compare_token(self, token_a, token_b) -> bool:
        return self.synonyms_gen.compare_words(str(token_a), str(token_b))
    
    def reset(self) -> 'MatchSimple':
        self.matched_exact = 0
        self.tokens_parsed = 0
        super().reset()
        return self

    def match(self, word_a: str, word_b: str) -> MatchType:
        tokens_a = self.tokenizer(word_a)
        tokens_b = self.tokenizer(word_b)

        for (token_a, token_b) in itertools.zip_longest(tokens_a, tokens_b, fillvalue=None):
            is_matched = self._compare_token(token_a, token_b)
            if is_matched:
                self.matched_exact += 1

            self.tokens_parsed += 1

        return MatchType(self.matched_exact / self.tokens_parsed, word_a, word_b)

class RelaxedMatchingBase():
    __slots__ = []

    def _eat_nonsense_tokens(self, tokens: List['Token']) -> List['Token']:
        rets = []
        
        gen = SynonymsGen(gen_without_vowels=True)
        nonsense = ['_', '', ' '] + self.names + [name.rstrip(string.digits) for name in self.names]
        for token in tokens:
            if not any(gen.compare_words(element, str(token)) for element in nonsense):
                rets.append(token)
        return rets

    # defunct for now
    def _transform_tokens(self, tokens: List['Token']) -> List['Token']:
        rets = []

        for token in tokens:
            # capital R at the end is likely short for "Register" - remove it
            if str(token).endswith('R'):
                rets.append(token.token[:-1])
                print('Edited token:', token)
            else:
                rets.append(token)
        return rets

    def _strict_comparator(self, a: str, b: str):
        a = a.lower()
        b = b.lower()
        if a == b:
            return True
        return False

    def _loose_comparator(self, a: str, b: str) -> bool:
        a = a.lower()
        b = b.lower()
        if self._strict_comparator(a, b):
            return True

        # if not matched exactly, see if Levenshtein works
        # use combination of ratio and distance to define a cutoff

        # Levenshtein "detects" deletions, insertions and substitutions
        # so dist equal to X means that X of these operations occurred to transform text from a -> b
        # ratio shows how much both strings are similar - especially for short names, ratio can be very low even if distance is low
        # this way it can mean that the whole name changed
        dist = Levenshtein.distance(a, b)
        ratio = Levenshtein.normalized_similarity(a, b)
        
        # above this cutoff (empirically found) maybe yes
        if dist < 3 and ratio >= 0.75:
            print(f'Warn: Match with Levenshtein! ratio: {round(ratio, 2)} distance: {dist}, tokens:', a, b)
            # Levenshtein is still kinda arbitrary, so it would be good to warn user about its use
            self.levenshtein_used = True
            return ratio
        return False

class MatchRelaxed(Comparator, RelaxedMatchingBase):
    __slots__ = ['matched_exact', 'tokens_parsed', 'levenshtein_used', 'is_fuzzy']

    def __init__(self, is_fuzzy: bool = False) -> None:
        self.is_fuzzy = is_fuzzy
        super().__init__()

    def _compare_token(self, token_a, token_b) -> bool:
        return SynonymsGen(comparator=super()._loose_comparator if self.is_fuzzy else super()._strict_comparator, gen_without_vowels=True).compare_words(str(token_a), str(token_b))

    def reset(self) -> 'MatchRelaxed':
        self.matched_exact = 0
        self.tokens_parsed = 0
        self.levenshtein_used = False
        super().reset()
        return self

    def match(self, word_a: str, word_b: str) -> MatchType:
        tokens_a, tokens_b = self.tokenizer(word_a), self.tokenizer(word_b)
        tokens_a, tokens_b = self._eat_nonsense_tokens(tokens_a), self._eat_nonsense_tokens(tokens_b)

        itoken_a, itoken_b = iter(tokens_a), iter(tokens_b)

        max_total_tokens = max(len(tokens_a), len(tokens_b))

        try:
            while True:
                token_a, token_b = next(itoken_a), next(itoken_b)

                is_matched = self._compare_token(token_a, token_b)
                if is_matched:
                    print(token_a, 'and', token_b, 'match.')
                    self.matched_exact += 1
                else:
                    print(token_a, 'and', token_b, "don't match.")

                self.tokens_parsed += 1

        except StopIteration:
            pass

        return MatchType(self.matched_exact / max_total_tokens, word_a, word_b, self.is_fuzzy) if self.tokens_parsed > 0 else MatchType.NoMatch(word_a, word_b)

class MatchFirstLetters(Comparator, RelaxedMatchingBase):
    def __init__(self, is_fuzzy: bool = False) -> None:
        self.is_fuzzy = is_fuzzy
        super().__init__()

    def _get_sentences(self, tokens_source):
        syns = [list(self.synonyms_gen.get_word_synonyms(str(token))) for token in tokens_source]
        sentences = WordGen().add_tokens(*syns).gen_possible_words()
        return sentences

    def _do_comparison(self, sentences_a, sentences_b):
        def join_first_letters(tokens: List['Token']):
            return ''.join([str(token)[0] for token in tokens])

        def join_and_synonyms(tokens: List['Token']):
            joined = []

            # trick: expand first word, join first letters of latter - this happens sometimes
            # actually, do it for every word
            for pos in range(0, len(tokens)):
                for syn in self.synonyms_gen.get_word_synonyms(str(tokens[pos])):
                    joined += [join_first_letters(tokens[0:pos]) + syn + join_first_letters(tokens[pos + 1:])]

            return joined

        #print('s', sentences_a, ':', sentences_b)
        for words_a in sentences_a:
            joined_a = [join_first_letters(words_a)] + [''.join(words_a)] + join_and_synonyms(words_a)
            for words_b in sentences_b:
                joined_b = [join_first_letters(words_b)] + [''.join(words_b)] + join_and_synonyms(words_b)
                #print('j', joined_a, ':', joined_b)
                for join_a in joined_a:
                    for join_b in joined_b:
                        #print(join_a, 'vs', join_b)
                        if len(join_a) > 1 and len(join_b) > 1 and (ratio := self._internal_cmpr(join_a, join_b)):
                            print('Possible match:', join_a, 'and', join_b)
                            if self.is_fuzzy:
                                yield MatchType(ratio, join_a, join_b, self.is_fuzzy)
                            else:
                                yield MatchType.Exact(join_a, join_b)
                        else:
                            yield MatchType.NoMatch(join_a, join_b)

    def match(self, word_a: str, word_b: str) -> MatchType:
        self._internal_cmpr = self._loose_comparator if self.is_fuzzy else self._strict_comparator

        tokens_a, tokens_b = self.tokenizer(word_a), self.tokenizer(word_b)
        tokens_a, tokens_b = self._eat_nonsense_tokens(tokens_a), self._eat_nonsense_tokens(tokens_b)

        # don't do that if both tokenize nicely
        #if len(tokens_a) > len(tokens_b) - 2 and len(tokens_a) < len(tokens_b) + 2:
        #    return MatchType.NoMatch(word_a, word_b)

        sentences_a = self._get_sentences(tokens_a)
        sentences_b = self._get_sentences(tokens_b)

        matches = self._do_comparison(sentences_a, sentences_b)

        return next(filter(lambda m: m > MatchType.NoMatch(word_a, word_b), matches), MatchType.NoMatch(word_a, word_b))

class RegCompare:

    CompareResult = namedtuple('CompareResult', ['PeripheralPair', 'LeftHeavy', 'RightHeavy', 'Unmatched', 'Avg'])

    def __init__(self, regs1: RegistersGroup, regs2: RegistersGroup) -> None:
        self.regs1 = regs1
        self.regs2 = regs2

    def compare_register_group_layout(self, match_names = True) -> 'CompareResult':
        def create_dict(regs: RegistersGroup) -> dict[int, Register]:
            return {reg.Offset: reg for reg in regs}

        if match_names:
            cmpr = WordCompare(
                synonyms_gen=SynonymsGen(gen_without_vowels=True),
                names=[self.regs1.peripheral_name, self.regs2.peripheral_name]
            )
            
            cmpr.register_comparator(MatchSimple()).register_comparator(MatchRelaxed()).register_comparator(MatchFirstLetters())
            cmpr.register_comparator(MatchRelaxed(True)).register_comparator(MatchFirstLetters(True))

        offs1, offs2 = create_dict(self.regs1), create_dict(self.regs2)

        reg1_heavy, reg2_heavy = 0, 0
        for (off, reg) in offs2.items():
            if off in offs1:
                print('++ Register', reg.Name, 'has the same offset as', offs1[off].Name, f'({hex(reg.Offset)})')
                if (w1 := reg.get_width()) == (w2 := offs1[off].get_width()):
                    print('++ Registers have equal width:', w1)
                else:
                    print('++ Register has width', w1, 'vs width', w2)
                if match_names :
                    for cmp_name, match_certainty in cmpr.run_compare(reg.Name, offs1[off].Name, fallback_fuzzy=True):
                        print(f'+ Match {cmp_name}?', match_certainty)
            else:
                print('[1] Register', reg.Name, 'has no equivalent on the same offset:', hex(reg.Offset))
                reg1_heavy += 1

            print()
        for (off, reg) in offs1.items():
            if not off in offs2:
                print('[2] Register', reg.Name, 'has no equivalent on the same offset:', hex(reg.Offset))
                reg2_heavy += 1

        print('----')

        if reg1_heavy or reg2_heavy:
            print(f'Left has {reg1_heavy} more registers vs right {reg2_heavy} more.')

        if match_names:
            for (names, val) in cmpr.best_matches.items():
                if val.match == 0:
                    continue
                print('Best match for:', names, 'certainty', val.match, '%', val.comp_name, '[FZ]' if val.fuzzy_match else '')

            unmatched_cnt = len(cmpr.get_unmatched())
            print(f'Not matched at all [{unmatched_cnt} / {cmpr.runs}]:', cmpr.get_unmatched())

            # 0 runs mean nothing was matched, because offsets don't match
            unmatched = 100 if cmpr.runs == 0 else (unmatched_cnt * 100) / cmpr.runs
            
            matches_cnt = len(cmpr.best_matches.values())
            avg_certainty = 0 if matches_cnt == 0 else sum(i.match for i in cmpr.best_matches.values()) / matches_cnt
            
            return RegCompare.CompareResult((self.regs1.peripheral_name, self.regs2.peripheral_name), reg1_heavy, reg2_heavy, unmatched, avg_certainty)

        return None
