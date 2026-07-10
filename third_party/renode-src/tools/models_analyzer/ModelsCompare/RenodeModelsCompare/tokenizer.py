#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
import re
from typing import Iterator, List

class Token:
    def __init__(self, token: str, atomic: bool = False) -> None:
        if not isinstance(token, str):
            raise TypeError("Token has to be a string")
        self.token = token
        self.atomic = atomic

    def __str__(self) -> str:
        return self.token
    
    def __repr__(self) -> str:
        return f"<{self.token}, {'a' if self.atomic else 'n'}>"
    
    @classmethod
    def from_iterator(cls, words: Iterator, atomic: bool = False) -> Iterator:
        for word in words:
            if isinstance(word, cls):
                yield word
            else:
                yield cls(word, atomic)

class Tokenizer:
    '''
    New tokenizers should inherit from this class
    To tokenize words __call__ method should be used.
    '''
    @staticmethod
    def _rem_empty(array):
        return (i for i in array if str(i) != '')
    
    def __call__(self, word: str) -> Iterator[Token]:
        raise NotImplementedError("Tokenizer is an abstract class")

class CaseTokenizer(Tokenizer):
    '''
    This tokenizer splits words based on character case change
    '''
    def __call__(self, word: str) -> Iterator[Token]:
        return Token.from_iterator(super()._rem_empty(re.split(r'([A-Z][a-z]+)', word)))

class UnderscoreTokenizer(Tokenizer):
    '''
    This tokenizer splits based on underscores
    '''
    def __call__(self, word: str) -> Iterator[Token]:
        return Token.from_iterator(super()._rem_empty(re.split(r'(_+)', word)))

class DotTokenizer(Tokenizer):
    '''
    This tokenizer splits based on dots
    '''
    def __call__(self, word: str) -> Iterator[Token]:
        return Token.from_iterator(super()._rem_empty(re.split(r'(\.+)', word)))

class NumberTokenizer(Tokenizer):
    '''
    This tokenizer splits based on existence of numbers
    '''
    def __call__(self, word: str) -> Iterator[Token]:
        return Token.from_iterator(super()._rem_empty(re.split(r'([0-9]+)', word)))
    
class NameTokenizer(Tokenizer):
    '''
    This tokenizer splits based on predefined list of names (e.g. I2C)
    and marks found names as "atomic" tokens, meaning that they should not be split any further
    (e.g. NumberTokenizer would otherwise split I2C into I, 2, C)
    '''
    def __init__(self, names: List[str]) -> None:
        super().__init__()
        self._names = names

    def __call__(self, word: str) -> Iterator[Token]:
        words = [Token(word)]
        for name in self._names:
            new_words = []
            for word in words:
                new_words += re.split(rf'({name})', str(word)) if not word.atomic else [word]
                words = [*Token.from_iterator(super()._rem_empty(new_words))]
            for token in words:
                if token.token in self._names:
                    token.atomic = True

        return words

class TokenizerPipeline():
    '''
    Use this class to register tokenizer order and run tokenizers on words.
    Order in the pipeline makes a great difference.
    NameTokenizer should probably be first in the pipeline.
    '''
    def __init__(self) -> None:
        self._tokenizers = []

    def add_tokenizer(self, *tokenizers: Tokenizer) -> 'TokenizerPipeline':
        self._tokenizers.extend(tokenizers)
        return self
    
    def prepend_tokenizer(self, tokenizer) -> 'TokenizerPipeline':
        self._tokenizers = [tokenizer] + self._tokenizers
        return self

    def run_pipeline(self, word: str) -> None:
        self.tokens = [Token(word)]
        for tokenizer in self._tokenizers:
            new_tokens = []
            for token in self.tokens:
                new_tokens += tokenizer(str(token)) if token.atomic != True else [token]
            self.tokens = [*new_tokens]
    
    def get_tokens(self) -> List[Token]:
        return self.tokens
    
    def __call__(self, word: str) -> List[Token]:
        self.run_pipeline(word)
        return self.tokens

_renodeNameTokenizer = NameTokenizer(['GPIO', 'I2C', 'IRQ', 'PCIExpress', 'PCI', 'SPI'])

def get_default_tokenizer_pipeline():
    return TokenizerPipeline().add_tokenizer(_renodeNameTokenizer, UnderscoreTokenizer(), DotTokenizer(), NumberTokenizer(), CaseTokenizer())
