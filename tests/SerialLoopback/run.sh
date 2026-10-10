#!/usr/bin/env bash
# 真实串口回环测试（Linux，需要 socat 和 python3）。
# 场景：A 有回显、B 无回显、C 电表只认实际表号（不响应公共地址读数据）。
set -u
DIR="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
dotnet build "$DIR/Harness/Harness.csproj" -c Release -o "$WORK/bin" -v q >/dev/null || { echo "编译失败"; exit 1; }
FAIL=0
run_case() { # $1 名称 $2 回显 $3 接受公共地址读数据
  echo "########## $1"
  rm -f "$WORK/vA" "$WORK/vB" "$WORK/meter.log"
  socat pty,raw,echo=0,link="$WORK/vA" pty,raw,echo=0,link="$WORK/vB" 2>/dev/null &
  local socat_pid=$!
  sleep 1
  python3 -I "$DIR/fakemeter.py" "$WORK/vB" "$2" "$3" "$WORK/meter.log" &
  local meter_pid=$!
  sleep 0.5
  timeout 60 dotnet "$WORK/bin/Harness.dll" "$(readlink -f "$WORK/vA")" "$3" || FAIL=1
  kill "$meter_pid" "$socat_pid" 2>/dev/null; wait 2>/dev/null
  echo "---- 假电表日志 ----"; cat "$WORK/meter.log"; echo
}
run_case "A：红外头有回显" 1 1
run_case "B：无回显（RS485 / 不回显的红外头）" 0 1
run_case "C：电表只认实际表号" 1 0
rm -rf "$WORK"
[ "$FAIL" = 0 ] && echo "==== 串口回环测试全部通过 ====" || echo "==== 串口回环测试失败 ===="
exit $FAIL
