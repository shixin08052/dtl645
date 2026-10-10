"""独立实现的假电表：按 DL/T 645-2007 解析请求并应答（不复用被测软件的任何代码）。
用法: python3 fakemeter.py <pty路径> <echo:0/1> <accept_wildcard_read:0/1> <logfile>"""
import os, sys, termios, time, tty

path, echo, wildcard_ok, logf = sys.argv[1], sys.argv[2] == '1', sys.argv[3] == '1', sys.argv[4]
ADDR = bytes([0x78, 0x56, 0x34, 0x12, 0x00, 0x00])  # 表号 000012345678，A0 在前
fd = os.open(path, os.O_RDWR | os.O_NOCTTY)
tty.setraw(fd)
log = open(logf, 'a', buffering=1)

def hx(b): return ' '.join('%02X' % x for x in b)

def frame(ctrl, data):
    body = bytes([0x68]) + ADDR + bytes([0x68, ctrl, len(data)]) + bytes((d + 0x33) & 0xFF for d in data)
    return b'\xFE\xFE' + body + bytes([sum(body) & 0xFF, 0x16])

def send(b):
    log.write('METER TX ' + hx(b) + '\n')
    for x in b:                      # 逐字节发送，模拟 1200bps 约 9ms/字节
        os.write(fd, bytes([x])); time.sleep(0.009)

buf = b''
while True:
    chunk = os.read(fd, 256)
    if not chunk: continue
    if echo: os.write(fd, chunk)     # 红外半双工回显
    buf += chunk
    while True:
        i = buf.find(b'\x68')
        if i < 0: buf = b''; break
        if len(buf) - i < 10: buf = buf[i:]; break
        L = buf[i + 9]
        if len(buf) - i < 12 + L: buf = buf[i:]; break
        f = buf[i:i + 12 + L]; buf = buf[i + 12 + L:]
        if f[7] != 0x68 or f[-1] != 0x16 or (sum(f[:10 + L]) & 0xFF) != f[10 + L]:
            log.write('METER BAD ' + hx(f) + '\n'); continue
        log.write('METER RX ' + hx(f) + '\n')
        addr, ctrl = f[1:7], f[8]
        data = bytes((d - 0x33) & 0xFF for d in f[10:10 + L])
        time.sleep(0.05)
        if ctrl == 0x13 and addr == b'\xAA' * 6:
            send(frame(0x93, ADDR))
        elif ctrl == 0x11:
            exact = addr == ADDR
            wild = all(a == 0xAA or a == b for a, b in zip(addr, ADDR)) and addr != ADDR
            if not (exact or (wild and wildcard_ok)):
                log.write('METER IGNORE (地址不匹配或不接受通配读数据)\n'); continue
            di = data[:4]
            if di in (bytes([0, 0, 1, 0]), bytes([0, 0, 0, 0])):   # 00010000 / 00000000
                send(frame(0x91, di + bytes([0x56, 0x34, 0x12, 0x00])))  # 1234.56 kWh
            else:
                send(frame(0xD1, bytes([0x02])))
