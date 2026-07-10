#
# Copyright (c) 2022-2023 Antmicro
#
# This file is licensed under the Apache License 2.0.
# Full license text is available in 'LICENSE'.
#
from typing import List

from RenodeModelsCompare.regcompare import RegCompare

def match_most_similar(comparison_results: 'List[RegCompare.CompareResult]') -> None:
    import skcriteria as skc
    from skcriteria.preprocessing import invert_objectives, scalers
    from skcriteria.madm.similarity import TOPSIS

    data = []
    keys = []
    for result in comparison_results:
        keys += [result.PeripheralPair]
        data += [result[1:]]

    dm = skc.mkdm(
        data,
        objectives=["min", "min", "min", "max"],
        weights=[0.5, 0.7, 1, 1.5],
        alternatives=keys,
        criteria=['LeftHeavy', 'RightHeavy', 'Unmatched', 'Avg Match Certainty'],
    )
    print('Criteria and alternatives')
    print(dm)

    # can't make decision with nothing to compare between
    if len(data) < 2:
        return comparison_results[0]

    dmt = invert_objectives.NegateMinimize().transform(dm)

    dmt = scalers.SumScaler(target='weights').transform(dmt)
    dmt = scalers.VectorScaler(target='matrix').transform(dmt)

    model = TOPSIS()
    print('----', type(model).__name__)
    res = model.evaluate(dmt)

    # without to_string it displays incorrectly in CI
    print(res._result_series.sort_values().to_string())

    print('###############')
    print('Decision:')
    best = res._result_series.idxmin()
    print(best)

    return best

