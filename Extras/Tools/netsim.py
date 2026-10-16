"""
UDP-прокси с имитацией плохой сети для тестов Rocket Wars.

Клиент подключается к прокси вместо хоста; прокси пересылает пакеты в обе стороны
с задержкой, джиттером и потерей пакетов (в каждую сторону отдельно, т.е. RTT ≈ 2 × delay).

Пример: хост на 7777, клиенты подключаются к 7790 (-join 127.0.0.1 -port 7790):
    python netsim.py --listen 7790 --target 127.0.0.1:7777 --delay 75 --jitter 20 --loss 2
"""
import argparse
import asyncio
import random
import time


class Stats:
    def __init__(self):
        self.forwarded = 0
        self.dropped = 0


class Upstream(asyncio.DatagramProtocol):
    """Сокет к хосту для одного клиента."""

    def __init__(self, proxy, client_addr):
        self.proxy = proxy
        self.client_addr = client_addr
        self.transport = None
        self.last_seen = time.monotonic()

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        self.proxy.schedule(lambda: self.proxy.transport.sendto(data, self.client_addr))


class Proxy(asyncio.DatagramProtocol):
    def __init__(self, target, delay_ms, jitter_ms, loss_pct):
        self.target = target
        self.delay = delay_ms / 1000.0
        self.jitter = jitter_ms / 1000.0
        self.loss = loss_pct / 100.0
        self.transport = None
        self.clients = {}
        self.stats = Stats()
        self.loop = asyncio.get_event_loop()

    def connection_made(self, transport):
        self.transport = transport

    def schedule(self, send):
        if random.random() < self.loss:
            self.stats.dropped += 1
            return
        self.stats.forwarded += 1
        d = max(0.0, self.delay + random.uniform(-self.jitter, self.jitter))
        self.loop.call_later(d, send)

    def datagram_received(self, data, addr):
        up = self.clients.get(addr)
        if up is None:
            up = Upstream(self, addr)
            self.clients[addr] = up
            self.loop.create_task(self.loop.create_datagram_endpoint(lambda: up, remote_addr=self.target))
            print(f"[netsim] new client {addr}", flush=True)
        up.last_seen = time.monotonic()
        if up.transport is None:
            # сокет к хосту ещё открывается — повторим чуть позже
            self.loop.call_later(0.01, lambda: self.datagram_received(data, addr))
            return
        self.schedule(lambda: up.transport.sendto(data))


async def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--listen", type=int, default=7790)
    ap.add_argument("--target", default="127.0.0.1:7777")
    ap.add_argument("--delay", type=float, default=75, help="задержка в одну сторону, мс")
    ap.add_argument("--jitter", type=float, default=20, help="± разброс задержки, мс")
    ap.add_argument("--loss", type=float, default=2, help="потеря пакетов, %% в каждую сторону")
    args = ap.parse_args()

    host, port = args.target.split(":")
    loop = asyncio.get_event_loop()
    proxy = Proxy((host, int(port)), args.delay, args.jitter, args.loss)
    await loop.create_datagram_endpoint(lambda: proxy, local_addr=("127.0.0.1", args.listen))
    print(f"[netsim] {args.listen} -> {args.target} delay {args.delay}±{args.jitter} ms, loss {args.loss}%", flush=True)
    while True:
        await asyncio.sleep(10)
        print(f"[netsim] forwarded {proxy.stats.forwarded}, dropped {proxy.stats.dropped}, clients {len(proxy.clients)}", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
