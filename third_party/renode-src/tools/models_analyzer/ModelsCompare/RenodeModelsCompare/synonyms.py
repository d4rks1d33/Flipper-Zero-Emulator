#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import re
from typing import Iterator, List

class SynonymsGen:
    '''
    Returns synonyms for words, and checks if any of synonyms match
    Can be given a custom comparator that would be used in matching synonyms with words and each other
    Since it is a practice sometimes, has a built-in regex to optionally remove vowels from words
    '''
    SYNONYMS = [
        ['Enable', 'Enabled', 'En'],
        ['Transmit', 'Tx', "Xmit", 'Transmitting'],
        ['Receive', 'Rx', 'Recv', 'Receiving'],
        ['Short', 'Shortcuts', 'Shorts'],
        ['Device', 'Dev'],
        ['Offset', 'Off'],
        ['Control', 'Ctrl', 'Ctr', 'Cr'],
        ['Interrupt', 'Int', 'Intc', 'Irq'],
        ['Clear', 'Clr'],
        ['Toggle', 'Tog'],
        ['Physical', 'Phy', 'Ph'],
        ['Number', 'Num'],
        ['Threshold', 'Th', 'Tr'],
        ['Identifier', 'Id', 'Iden', 'Ident'],
        ['High', 'Hi', 'H'],
        ['Low', 'Lo', 'L'],
        ['Clock', 'Clk'],
        ['Backup', 'Bkp'],
        ['Wakeup', 'Wu'],
        ['Timestamp','Tst', 'Ts'],
        ['Prescaler', 'Pre'],
        ['Config', 'Cfg'],
        ['Command', 'Cmd'],
        ['Register', 'Reg'],
        []
    ]

    _vowelsRegex = r'[aeiou]'

    def __init__(self, comparator = None, gen_without_vowels: bool = False):
        self.comparator = comparator if comparator is not None else lambda a, b: a.lower() == b.lower()
        self.delete_vowels = gen_without_vowels
        self.SYNONYMS = type(self).SYNONYMS.copy()
        
        self.vowels_cutoff = 1 # prevent generating one-letter words, unless explicitly stated in SYNONYMS array

    @staticmethod
    def _remove_vowels(word) -> str:
        return re.sub(SynonymsGen._vowelsRegex, '', word, flags=re.IGNORECASE)

    @staticmethod
    def _memberwise_compare(a, b, comparator) -> Iterator[bool]:
        for i in a:
            for j in b:
                #print(i, j, comparator(i, j))
                yield comparator(i, j)

    def get_word_synonyms(self, a: str) -> Iterator[str]:
        def _delete_vowels_in_row(row):
            for word in row:
                yield word
                if self.delete_vowels:
                    helper = self._remove_vowels(word)
                    if len(helper) > self.vowels_cutoff:
                        yield helper

        yield a
        if self.delete_vowels:
            a_del = self._remove_vowels(a)
            if not self.comparator(a_del, a):
                if len(a_del) > self.vowels_cutoff:
                    yield a_del
        for obj in filter(
            lambda row: any(self.comparator(a, s) or (self.delete_vowels and self.comparator(a_del, s))
            for s in list(_delete_vowels_in_row(row))
        ), self.SYNONYMS):
            for w in obj:
                # eliminate duplicates by comparing with the base word
                if not self.delete_vowels:
                    if not self.comparator(w, a):
                        yield w
                else:
                    if not self.comparator(w, a_del) and not self.comparator(w, a):
                        yield w
                    helper = self._remove_vowels(w)
                    if not self.comparator(helper, w) and not self.comparator(helper, a) and not self.comparator(helper, a_del):
                        if len(a_del) > self.vowels_cutoff:
                            yield helper
    
    def compare_words(self, a: str, b: str) -> bool:
        sa = self.get_word_synonyms(a)
        # we need them expanded, because of double loop
        sb = list(self.get_word_synonyms(b))

        return any(self._memberwise_compare(sa, sb, self.comparator))

class WordGen:
    def __init__(self) -> None:
        self.reset()

    def reset(self) -> None:
        self.sentences = []

    def add_tokens(self, *bag_of_tokens: List[str]) -> 'WordGen':
        for tokens in bag_of_tokens:
            if not self.sentences:
                for token in tokens:
                    self.sentences.append([token])
                continue

            new_sentences = []
            for token in tokens:
                for sentence in self.sentences:
                    new_sentences += [sentence + [token]]
            self.sentences = new_sentences
        return self

    def gen_possible_words_concat(self) -> List[str]:
        return [''.join(i) for i in self.gen_possible_words()]

    def gen_possible_words(self) -> List[List[str]]:
        return self.sentences


def compare_words_exact(a: str, b: str) -> bool:
    return b == a

def compare_words_substr(a: str, b: str) -> bool:
    return (b in a) or (a in b)
