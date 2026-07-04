# Generic read/write-back peripheral stub (Renode request-based API).
if request.IsInit:
    regs = {}
elif request.IsRead:
    request.Value = regs.get(request.Offset, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
