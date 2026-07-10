{
    gsub(/\\/, "/")
    if (match($0, /.*?".*?\*\*\/\*\.cs".*/) != 0) {
        match($0, /^[^"]*/)
        prefix = substr($0, RSTART, RLENGTH)
        path_index = RSTART + RLENGTH + 1
        match($0, /[^"]*$/)
        suffix = substr($0, RSTART, RLENGTH)
        path_length = RSTART - path_index - 8
        path = substr($0, path_index, path_length)
        cmd = sprintf("find . -type f | grep ^\\./%s | grep \\.cs$ | sed -e 's|^\\./|%s\"|' | sed 's|/|\\\\|g' | sed -e 's|$|\"%s|'", path, prefix, suffix)
        system(cmd)
    } else {
        print
    }
}
