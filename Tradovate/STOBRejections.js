// Short-Term Order Block Rejections (ST OB Rejections), versión Tradovate Custom Indicator.
// Port de Tradingview/Short-Term Order Block Rejections.pine (MPL-2.0): bloques anclados al origen del tramo del CHoCH
// con desplazamiento (FVG), señales de rechazo, filtro de alta probabilidad (pendiente EMA + desplazamiento en ATR),
// trades con TP1 parcial / break-even y sus estadísticas, y VWAP anclado con bandas.
// Toda la lógica se evalúa sobre barras cerradas (como barstate.isconfirmed en Pine): nada repinta.
// Los dibujos salen por tramos de barra, así que los bloques mitigados no se desvanecen y no hay alertas.
const predef = require("./tools/predef");
const meta = require("./tools/meta");
const { px, du, op } = require("./tools/graphics");

const BULL = "#089981";
const BEAR = "#f23645";
const GRAY = "#808080";
const KEEP_BARS = 1500;   // barras cerradas guardadas (búsqueda del bloque, pendiente EMA)

// --- Zonas horarias sin Intl: reglas de horario de verano en el código ---
function nthSunday(y, m, n) {
    const first = new Date(Date.UTC(y, m, 1)).getUTCDay();
    return 1 + ((7 - first) % 7) + (n - 1) * 7;
}
function lastSunday(y, m) {
    const last = new Date(Date.UTC(y, m + 1, 0));
    return last.getUTCDate() - last.getUTCDay();
}
// Desplazamiento respecto a UTC, en minutos
function tzOffset(ms, tz) {
    const y = new Date(ms).getUTCFullYear();
    let s, e;
    switch (tz) {
        case "America/New_York":
            s = Date.UTC(y, 2, nthSunday(y, 2, 2), 7); e = Date.UTC(y, 10, nthSunday(y, 10, 1), 6);
            return ms >= s && ms < e ? -240 : -300;
        case "America/Chicago":
            s = Date.UTC(y, 2, nthSunday(y, 2, 2), 8); e = Date.UTC(y, 10, nthSunday(y, 10, 1), 7);
            return ms >= s && ms < e ? -300 : -360;
        case "America/Los_Angeles":
            s = Date.UTC(y, 2, nthSunday(y, 2, 2), 10); e = Date.UTC(y, 10, nthSunday(y, 10, 1), 9);
            return ms >= s && ms < e ? -420 : -480;
        case "America/Sao_Paulo": return -180;
        case "Europe/London":
            s = Date.UTC(y, 2, lastSunday(y, 2), 1); e = Date.UTC(y, 9, lastSunday(y, 9), 1);
            return ms >= s && ms < e ? 60 : 0;
        case "Europe/Berlin":
            s = Date.UTC(y, 2, lastSunday(y, 2), 1); e = Date.UTC(y, 9, lastSunday(y, 9), 1);
            return ms >= s && ms < e ? 120 : 60;
        case "Asia/Tokyo": return 540;
        case "Asia/Hong_Kong":
        case "Asia/Singapore": return 480;
        case "Australia/Sydney":
            // Verano austral: del primer domingo de octubre al primer domingo de abril (16:00 UTC del sábado)
            e = Date.UTC(y, 3, nthSunday(y, 3, 1) - 1, 16); s = Date.UTC(y, 9, nthSunday(y, 9, 1) - 1, 16);
            return ms < e || ms >= s ? 660 : 600;
        default: return 0;
    }
}
function localMinute(ms, tz) {
    const t = new Date(ms + tzOffset(ms, tz) * 60000);
    return t.getUTCHours() * 60 + t.getUTCMinutes();
}
// "HHMM-HHMM" -> [inicio, fin] en minutos del día
function parseSession(txt, fallback) {
    const m = /^\s*(\d{2})(\d{2})\s*-\s*(\d{2})(\d{2})\s*$/.exec(String(txt || ""));
    return m ? [Number(m[1]) * 60 + Number(m[2]), Number(m[3]) * 60 + Number(m[4])] : fallback;
}
function inSessionMin(m, sess) {
    const s = sess[0], e = sess[1];
    if (s === e) return true;
    return s < e ? m >= s && m < e : m >= s || m < e;
}

// --- Medias con semilla SMA, como ta.ema / ta.rma ---
function avgState() { return { n: 0, sum: 0, val: NaN }; }
function avgPush(st, x, len, alpha) {
    if (st.n < len) {
        st.sum += x;
        st.n += 1;
        st.val = st.n === len ? st.sum / len : NaN;
    }
    else st.val += alpha * (x - st.val);
    return st.val;
}
function avgPeek(st, x, len, alpha) {
    if (st.n < len) return st.n + 1 === len ? (st.sum + x) / len : NaN;
    return st.val + alpha * (x - st.val);
}

function num(x) { return typeof x === "number" && isFinite(x); }
function fmt(x, dec) { return num(x) ? x.toFixed(dec) : "n/a"; }
function signed(x, dec) { return (x > 0 ? "+" : "") + x.toFixed(dec); }
// Tradovate ignora el alfa de rgba(): la transparencia va en opacity, en porcentaje
function fill(hex, a) { return { color: hex, opacity: Math.round(a * 100) }; }

// Coordenadas: en Y, "-" px sube y "+" px baja respecto al precio
function at(x, y) { return { x: du(x), y: du(y) }; }
function above(x, y, pixels) { return { x: du(x), y: op(du(y), "-", px(pixels)) }; }
function below(x, y, pixels) { return { x: du(x), y: op(du(y), "+", px(pixels)) }; }

class stObRejections {
    init() {
        const p = this.props;
        this.tick = (this.contractInfo && this.contractInfo.tickSize) || 0.25;
        const cd = this.chartDescription;
        this.intraday = !cd || cd.underlyingType !== "DailyBar";
        this.barMs = cd && cd.underlyingType === "MinuteBar" && cd.elementSizeUnit === "UnderlyingUnits" ? cd.elementSize * 60000 : 0;
        this.signalSession = parseSession(p.signalSession, [570, 960]);

        // Barras cerradas: this.B[k - this.base]
        this.B = [];
        this.base = 0;
        this.curIdx = -1;
        this.pending = null;
        this.emit = [];
        this.seq = 0;

        this.atrHp = avgState();
        this.atrRange = avgState();
        this.emaF = avgState();
        this.emaS = avgState();

        this.lastSwingHigh = NaN; this.previousSwingHigh = NaN; this.lastSwingHighBar = -1;
        this.lastSwingLow = NaN; this.previousSwingLow = NaN; this.lastSwingLowBar = -1;
        this.structureTrend = 0;
        this.bullBlocks = [];
        this.bearBlocks = [];
        this.risk = null;

        this.trd = null;
        this.st = { n: 0, won: 0, r: 0, buy: 0, sell: 0, tp: 0, be: 0, sl: 0 };

        this.vw = { sv: 0, svs: 0, svs2: 0, inSess: false };
        this.lastBands = null;
    }

    get(k) { return this.B[k - this.base]; }

    // Clave estable por barra cerrada y orden de dibujo: el mismo dibujo recibe la misma clave en cada recálculo
    key() { this.seq += 1; return "g" + this.keyBar + "_" + this.seq; }

    // Clave de día de trading (aaaammdd) para reinicios diarios / semanales / mensuales
    dayKey(d, ms) {
        const td = typeof d.tradeDate === "function" ? d.tradeDate() : undefined;
        if (typeof td === "number" && td > 19000000) return td;
        if (typeof td === "string" && /^\d{8}$/.test(td)) return Number(td);
        // Sin tradeDate utilizable: día de Nueva York con cambio a las 18:00
        const t = new Date(ms + (tzOffset(ms, "America/New_York") + 360) * 60000);
        return t.getUTCFullYear() * 10000 + (t.getUTCMonth() + 1) * 100 + t.getUTCDate();
    }

    map(d, i, history) {
        const p = this.props;
        const ms = d.timestamp().getTime();
        const b = { o: d.open(), h: d.high(), l: d.low(), c: d.close(), v: d.volume() || 0, t: ms, td: this.dayKey(d, ms) };

        if (i < this.curIdx) this.init();          // recálculo completo
        if (i !== this.curIdx) {
            // La barra anterior acaba de cerrar: se procesa una sola vez
            this.emit = [];
            if (this.pending && this.curIdx >= 0) this.processBar(this.curIdx, this.pending);
            this.curIdx = i;
        }
        this.pending = b;

        const prev = i > 0 ? this.get(i - 1) : undefined;
        if (!this.barMs && prev) {
            const diff = b.t - prev.t;
            if (diff > 0) this.barMs = diff;
        }

        // Plots de la barra en formación
        const out = {};
        if (p.showTrendEmas) {
            const ef = avgPeek(this.emaF, b.c, p.fastEma, 2 / (p.fastEma + 1));
            const es = avgPeek(this.emaS, b.c, p.slowEma, 2 / (p.slowEma + 1));
            if (num(ef)) out.emaFast = ef;
            if (num(es)) out.emaSlow = es;
        }

        const items = this.emit.slice();
        const vr = this.vwapCalc(this.vw, b, prev);
        if (vr.visible && num(vr.value)) {
            const bands = this.bands(vr);
            out.vwap = bands.vwap;
            if (p.band1) { out.upper1 = bands.u1; out.lower1 = bands.l1; }
            if (p.band2) { out.upper2 = bands.u2; out.lower2 = bands.l2; }
            if (p.band3) { out.upper3 = bands.u3; out.lower3 = bands.l3; }
            if (p.fillBands && !vr.reset && this.lastBands) this.bandFills(items, i, this.lastBands, bands);
        }

        // Elementos globales con clave fija: cada barra los sustituye, queda la versión de la última
        if (p.tradeSignals && p.showStats) this.statsPanel(items, i, b.l);
        if (p.projectRisk && this.risk) this.riskProjection(items);

        if (items.length) out.graphics = { items };
        return out;
    }

    // --- Lógica principal sobre la barra cerrada k ---
    processBar(k, b) {
        const p = this.props;
        this.keyBar = k;
        this.seq = 0;
        const prev = k > 0 ? this.get(k - 1) : undefined;

        if (!this.barMs && prev && b.t > prev.t) this.barMs = b.t - prev.t;
        const tr = prev ? Math.max(b.h - b.l, Math.abs(b.h - prev.c), Math.abs(b.l - prev.c)) : b.h - b.l;
        b.atr = avgPush(this.atrHp, tr, 14, 1 / 14);
        b.ratr = avgPush(this.atrRange, tr, p.rangeAtrLength, 1 / p.rangeAtrLength);
        b.ef = avgPush(this.emaF, b.c, p.fastEma, 2 / (p.fastEma + 1));
        b.es = avgPush(this.emaS, b.c, p.slowEma, 2 / (p.slowEma + 1));

        this.B.push(b);
        if (this.B.length > KEEP_BARS + 500) {
            this.B.splice(0, 500);
            this.base += 500;
        }

        // VWAP de barras cerradas (estado acumulado y valores para el relleno de la barra siguiente)
        const vr = this.vwapCalc(this.vw, b, prev);
        this.vw = vr.next;
        this.lastBands = vr.visible && num(vr.value) ? this.bands(vr) : null;

        // --- Estructura de swings confirmados ---
        const L = p.pivotBars;
        if (k >= 2 * L) {
            const c = k - L, cb = this.get(c);
            let ph = true, pl = true;
            for (let j = 1; j <= L; j++) {
                const lf = this.get(c - j), rt = this.get(c + j);
                if (lf.h > cb.h || rt.h >= cb.h) ph = false;
                if (lf.l < cb.l || rt.l <= cb.l) pl = false;
            }
            if (ph) { this.previousSwingHigh = this.lastSwingHigh; this.lastSwingHigh = cb.h; this.lastSwingHighBar = c; }
            if (pl) { this.previousSwingLow = this.lastSwingLow; this.lastSwingLow = cb.l; this.lastSwingLowBar = c; }
        }

        const both = num(this.previousSwingHigh) && num(this.previousSwingLow);
        if (this.structureTrend === 0 && both) {
            if (this.lastSwingHigh > this.previousSwingHigh && this.lastSwingLow > this.previousSwingLow) this.structureTrend = 1;
            else if (this.lastSwingHigh < this.previousSwingHigh && this.lastSwingLow < this.previousSwingLow) this.structureTrend = -1;
        }

        // CHoCH: cierre a través del swing protegido contra la tendencia establecida
        const bullishChoch = !!prev && this.structureTrend === -1 && num(this.lastSwingHigh) && b.c > this.lastSwingHigh && prev.c <= this.lastSwingHigh;
        const bearishChoch = !!prev && this.structureTrend === 1 && num(this.lastSwingLow) && b.c < this.lastSwingLow && prev.c >= this.lastSwingLow;
        if (bullishChoch) this.structureTrend = 1;
        else if (bearishChoch) this.structureTrend = -1;

        const bullishStructureAllowed = !p.onlyWithStructure || this.structureTrend === 1;
        const bearishStructureAllowed = !p.onlyWithStructure || this.structureTrend === -1;

        if (p.showChoch && bullishChoch) this.drawChoch(true, this.lastSwingHighBar, this.lastSwingHigh, k);
        if (p.showChoch && bearishChoch) this.drawChoch(false, this.lastSwingLowBar, this.lastSwingLow, k);

        // --- Filtro de alta probabilidad ---
        const slopeOk = k >= p.fastSlopeBars && k >= p.slowSlopeBars && k - this.base >= Math.max(p.fastSlopeBars, p.slowSlopeBars);
        const fBack = slopeOk ? this.get(k - p.fastSlopeBars) : undefined;
        const sBack = slopeOk ? this.get(k - p.slowSlopeBars) : undefined;
        const hpTrendLong = !p.emaTrendFilter || (slopeOk && b.ef > fBack.ef && b.es > sBack.es);
        const hpTrendShort = !p.emaTrendFilter || (slopeOk && b.ef < fBack.ef && b.es < sBack.es);

        // --- Creación de bloques solo en CHoCH ---
        const stopBuffer = p.stopBufferTicks * this.tick;
        let chochBuy = false, chochSell = false;
        let chochBuyStop = NaN, chochSellStop = NaN, chochBuyDisp = NaN, chochSellDisp = NaN;

        if (bullishChoch) {
            const r = this.findBlock(k, 1, this.lastSwingHighBar);
            const legDisp = (b.c - r.origin) / b.atr;
            const withinSwing = !p.requireSwingRange || r.top < this.lastSwingHigh;
            const impulsive = !p.requireDisplacement || r.displacement;
            if (r.found && withinSwing && impulsive) {
                const dup = p.suppressOverlapping && (this.overlapsActive(k, r.top, r.bottom, this.bullBlocks) || this.overlapsActive(k, r.top, r.bottom, this.bearBlocks));
                if (!dup) {
                    const ob = { top: r.top, bottom: r.bottom, born: k, left: k - r.offset, extreme: b.h, used: false, disp: legDisp };
                    this.drawBlock(ob, true, ob.left, k + 1, true);
                    this.bullBlocks.push(ob);
                    if (this.bullBlocks.length > p.trackedBlocks) this.bullBlocks.shift();
                    chochBuy = true;
                    chochBuyStop = r.bottom - stopBuffer;
                    chochBuyDisp = legDisp;
                }
            }
        }

        if (bearishChoch) {
            const r = this.findBlock(k, -1, this.lastSwingLowBar);
            const legDisp = (r.origin - b.c) / b.atr;
            const withinSwing = !p.requireSwingRange || r.bottom > this.lastSwingLow;
            const impulsive = !p.requireDisplacement || r.displacement;
            if (r.found && withinSwing && impulsive) {
                const dup = p.suppressOverlapping && (this.overlapsActive(k, r.top, r.bottom, this.bearBlocks) || this.overlapsActive(k, r.top, r.bottom, this.bullBlocks));
                if (!dup) {
                    const ob = { top: r.top, bottom: r.bottom, born: k, left: k - r.offset, extreme: b.l, used: false, disp: legDisp };
                    this.drawBlock(ob, false, ob.left, k + 1, true);
                    this.bearBlocks.push(ob);
                    if (this.bearBlocks.length > p.trackedBlocks) this.bearBlocks.shift();
                    chochSell = true;
                    chochSellStop = r.top + stopBuffer;
                    chochSellDisp = legDisp;
                }
            }
        }

        // --- Señales de rechazo, extensión y mitigación ---
        const candleRange = Math.max(b.h - b.l, this.tick);
        const bodyPercent = Math.abs(b.c - b.o) / candleRange * 100;
        const lookback = Math.max(L * 2, 2);
        let priorLow = Infinity, priorHigh = -Infinity;
        const lookbackReady = k >= lookback;
        if (lookbackReady) {
            for (let j = 1; j <= lookback; j++) {
                const x = this.get(k - j);
                priorLow = Math.min(priorLow, x.l);
                priorHigh = Math.max(priorHigh, x.h);
            }
        }
        const bearishAtFreshLow = lookbackReady && b.l < priorLow;
        const bullishAtFreshHigh = lookbackReady && b.h > priorHigh;
        const sessionTime = !this.intraday || inSessionMin(localMinute(b.t, "America/New_York"), this.signalSession);
        const inSession = !p.signalsOnlyInSession || sessionTime;
        const touchMode = p.zoneInteraction;

        let bullishSignal = false, bullStop = NaN, bullTp1 = NaN, bullDisp = NaN;
        let bearishSignal = false, bearStop = NaN, bearTp1 = NaN, bearDisp = NaN;

        for (let n = this.bullBlocks.length - 1; n >= 0; n--) {
            const ob = this.bullBlocks[n];
            const age = k - ob.born;
            const mid = (ob.top + ob.bottom) / 2;
            const mitigated = age > 0 && b.c < ob.bottom;
            const reclaim = b.l < ob.bottom && b.c > ob.top;
            const touched = touchMode === "touch" ? b.l <= ob.top : touchMode === "midpoint" ? b.l <= mid : b.l < ob.bottom;
            const rejected = touchMode === "sweep" ? b.o > ob.bottom && b.c > ob.bottom : b.c > ob.top;
            const inWindow = age > 0 && age <= p.reactionWindow;
            const rangePass = !p.requirePivotRange || (num(this.lastSwingHigh) && num(b.ratr) && this.lastSwingHigh - ob.top >= b.ratr * p.minPivotRangeAtr);
            const signal = inSession && rangePass && (bullishStructureAllowed || reclaim) && (reclaim || !p.avoidFreshExtremes || !bullishAtFreshHigh)
                && inWindow && !ob.used && ((touched && rejected) || reclaim) && b.c > b.o && bodyPercent >= p.minBodyPercent;
            if (age >= 1 && age <= p.reactionWindow) this.drawBlock(ob, true, k, k + 1, false);
            if (signal) {
                ob.used = true;
                // El bloque más reciente que dispara fija los niveles
                if (!bullishSignal) {
                    bullStop = Math.min(b.l, ob.bottom) - stopBuffer;
                    bullTp1 = ob.extreme > b.c ? ob.extreme : NaN;
                    bullDisp = ob.disp;
                }
                bullishSignal = true;
            }
            ob.extreme = Math.max(ob.extreme, b.h);
            if (mitigated) this.bullBlocks.splice(n, 1);
        }

        for (let n = this.bearBlocks.length - 1; n >= 0; n--) {
            const ob = this.bearBlocks[n];
            const age = k - ob.born;
            const mid = (ob.top + ob.bottom) / 2;
            const mitigated = age > 0 && b.c > ob.top;
            const reclaim = b.h > ob.top && b.c < ob.bottom;
            const touched = touchMode === "touch" ? b.h >= ob.bottom : touchMode === "midpoint" ? b.h >= mid : b.h > ob.top;
            const rejected = touchMode === "sweep" ? b.o < ob.top && b.c < ob.top : b.c < ob.bottom;
            const inWindow = age > 0 && age <= p.reactionWindow;
            const rangePass = !p.requirePivotRange || (num(this.lastSwingLow) && num(b.ratr) && ob.bottom - this.lastSwingLow >= b.ratr * p.minPivotRangeAtr);
            const signal = inSession && rangePass && (bearishStructureAllowed || reclaim) && (reclaim || !p.avoidFreshExtremes || !bearishAtFreshLow)
                && inWindow && !ob.used && ((touched && rejected) || reclaim) && b.c < b.o && bodyPercent >= p.minBodyPercent;
            if (age >= 1 && age <= p.reactionWindow) this.drawBlock(ob, false, k, k + 1, false);
            if (signal) {
                ob.used = true;
                if (!bearishSignal) {
                    bearStop = Math.max(b.h, ob.top) + stopBuffer;
                    bearTp1 = ob.extreme < b.c ? ob.extreme : NaN;
                    bearDisp = ob.disp;
                }
                bearishSignal = true;
            }
            ob.extreme = Math.min(ob.extreme, b.l);
            if (mitigated) this.bearBlocks.splice(n, 1);
        }

        if (bullishSignal) this.markSignal(k, b, 1, bullStop, bullTp1);
        if (bearishSignal) this.markSignal(k, b, -1, bearStop, bearTp1);

        if (p.tradeSignals) this.manageTrades(k, b, {
            chochMode: p.tradeEntry === "choch",
            sigLong: p.tradeEntry === "choch" ? chochBuy : bullishSignal,
            sigShort: p.tradeEntry === "choch" ? chochSell : bearishSignal,
            chochBuyStop, chochSellStop, chochBuyDisp, chochSellDisp,
            bullStop, bearStop, bullTp1, bearTp1, bullDisp, bearDisp,
            hpTrendLong, hpTrendShort, sessionTime
        });
    }

    // Bloque de un tramo de CHoCH: origen = mínimo (máximo) desde el swing roto; bloque = vela opuesta más baja (alta)
    // desde el CHoCH hasta unas velas antes del origen; desplazamiento = FVG a favor entre el origen y el CHoCH
    findBlock(k, dir, swingBar) {
        const p = this.props;
        let legBars = Math.max(Math.min(k - swingBar, p.maxLegBars), 1);
        legBars = Math.min(legBars, k, k - this.base);
        const bar = i => this.get(k - i);
        let originOffset = 1;
        for (let i = 1; i <= legBars; i++)
            if (dir === 1 ? bar(i).l < bar(originOffset).l : bar(i).h > bar(originOffset).h) originOffset = i;
        let found = false, top = NaN, bottom = NaN, offset = -1;
        const limit = Math.min(originOffset + p.searchBeforeOrigin, legBars);
        for (let i = 1; i <= limit; i++) {
            const x = bar(i);
            const opposite = dir === 1 ? x.c < x.o : x.c > x.o;
            if (opposite && (!found || (dir === 1 ? x.l < bottom : x.h > top))) {
                top = x.h; bottom = x.l; offset = i; found = true;
            }
        }
        let displacement = false;
        for (let j = 0; j <= originOffset - 2; j++) {
            if (dir === 1 ? bar(j).l > bar(j + 2).h : bar(j).h < bar(j + 2).l) { displacement = true; break; }
        }
        return { found, top, bottom, offset, displacement, origin: dir === 1 ? bar(originOffset).l : bar(originOffset).h };
    }

    overlapsActive(k, newTop, newBottom, blocks) {
        const p = this.props;
        const newH = Math.max(newTop - newBottom, this.tick);
        for (const ob of blocks) {
            if (k - ob.born <= p.reactionWindow) {
                const h = Math.max(ob.top - ob.bottom, this.tick);
                const inter = Math.max(0, Math.min(newTop, ob.top) - Math.max(newBottom, ob.bottom));
                if (inter / Math.min(newH, h) * 100 >= p.overlapThreshold) return true;
            }
        }
        return false;
    }

    // --- Trades tomados de las señales ---
    manageTrades(k, b, s) {
        const p = this.props;
        let t = this.trd;
        let exitR = NaN, exitTag = "";

        if (t) this.drawTradeSegment(t, k - 1, k);

        // Stop / TP1 / objetivo / tiempo desde la vela siguiente a la entrada; si una vela toca stop y objetivo, cuenta el stop
        if (t && k > t.entryBar) {
            if (t.dir === 1 ? b.l <= t.stop : b.h >= t.stop) {
                exitTag = t.tp1Hit ? "BE" : "SL";
                exitR = this.closeTrade(t, k, t.stop, exitTag);
                t = null;
            }
            else {
                if (num(t.tp1) && !t.tp1Hit && (t.dir === 1 ? b.h >= t.tp1 : b.l <= t.tp1)) {
                    const part = p.tp1PartialPct / 100;
                    t.tp1Hit = true;
                    t.bank += part * (t.tp1 - t.entry) * t.dir / t.risk;
                    t.rem -= part;
                    if (p.beAfterTp1) t.stop = t.entry;
                    this.text(rightOf(k, t.tp1), "TP1", BULL, 10, "leftMiddle");
                }
                const hitTarget = t.dir === 1 ? b.h >= t.target : b.l <= t.target;
                const timedOut = p.maxBarsInTrade > 0 && k - t.entryBar >= p.maxBarsInTrade;
                if (t.rem <= 1e-9) {
                    exitTag = "TP1";
                    exitR = this.closeTrade(t, k, t.tp1, exitTag);
                    t = null;
                }
                else if (hitTarget || timedOut) {
                    exitTag = hitTarget ? "TP" : "Time";
                    exitR = this.closeTrade(t, k, hitTarget ? t.target : b.c, exitTag);
                    t = null;
                }
            }
        }

        // Modo CHoCH: opcionalmente un nuevo CHoCH + bloque cierra el trade a su cierre
        if (s.chochMode && p.exitOnNewChoch && t && s.sigLong !== s.sigShort) {
            exitTag = "CHoCH";
            exitR = this.closeTrade(t, k, b.c, exitTag);
            t = null;
        }

        if (num(exitR)) {
            const st = this.st;
            st.n += 1;
            st.won += exitR > 0 ? 1 : 0;
            st.r += exitR;
            st.tp += exitTag === "TP" || exitTag === "TP1" ? 1 : 0;
            st.be += exitTag === "BE" ? 1 : 0;
            st.sl += exitTag === "SL" ? 1 : 0;
        }

        // Entrada al cierre de la señal estando plano, si pasa el filtro de alta probabilidad
        if (!t && s.sigLong !== s.sigShort) {
            const dir = s.sigLong ? 1 : -1;
            const stop = s.chochMode ? (dir === 1 ? s.chochBuyStop : s.chochSellStop) : (dir === 1 ? s.bullStop : s.bearStop);
            const ext = dir === 1 ? s.bullTp1 : s.bearTp1;
            const disp = s.chochMode ? (dir === 1 ? s.chochBuyDisp : s.chochSellDisp) : (dir === 1 ? s.bullDisp : s.bearDisp);
            const risk = (b.c - stop) * dir;
            const filterOk = (dir === 1 ? s.hpTrendLong : s.hpTrendShort) && (!p.minLegDisplacement || disp >= p.minLegAtr)
                && (!p.tradesOnlyInSession || s.sessionTime);
            if (risk > 0 && filterOk) {
                const target = !s.chochMode && p.tradeTarget === "leg" && num(ext) ? ext : b.c + dir * p.targetR * risk;
                const tp1Px = b.c + dir * p.tp1R * risk;
                const useTp1 = (target - tp1Px) * dir > 0 && (p.tp1PartialPct > 0 || p.beAfterTp1);
                t = { dir, entry: b.c, stop, target, entryBar: k, risk, tp1: useTp1 ? tp1Px : NaN, tp1Hit: false, bank: 0, rem: 1 };
                if (dir === 1) this.st.buy += 1; else this.st.sell += 1;
                const c = dir === 1 ? BULL : BEAR;
                this.text(dir === 1 ? below(k, b.l, 26) : above(k, b.h, 26), dir === 1 ? "▲ BUY" : "▼ SELL", c, 12, "centerMiddle", "bold");
            }
        }
        this.trd = t;
    }

    closeTrade(t, k, exitPx, tag) {
        const exitR = t.bank + t.rem * (exitPx - t.entry) * t.dir / t.risk;
        this.text(rightOf(k, exitPx), tag + " " + signed(exitR, 2) + "R", exitR > 0 ? BULL : BEAR, 11, "leftMiddle", "bold");
        return exitR;
    }

    // --- Dibujos (salen en la barra siguiente a la barra cerrada que los produce) ---
    // Todos los dibujos van como global (como NukeBLOCKS / NukeTIMER): mezclar global y no global no dibujaba nada
    push(item) { item.global = true; this.emit.push(item); }

    text(point, txt, color, size, align, weight) {
        this.push({ tag: "Text", key: this.key(), point, text: txt, style: { fontSize: size, fill: color, fontWeight: weight || "normal" }, textAlignment: align });
    }

    line(x1, y1, x2, y2, color, width, dash) {
        this.push({ tag: "LineSegments", key: this.key(), lines: [{ tag: "Line", a: at(x1, y1), b: at(x2, y2) }],
            lineStyle: { lineWidth: width, color, lineStyle: dash || 1 } });
    }

    drawChoch(bull, swingBar, level, k) {
        const c = bull ? BULL : BEAR;
        this.line(swingBar, level, k, level, c, 1, 3);
        const mid = swingBar + Math.floor((k - swingBar) / 2);
        this.text(bull ? above(mid, level, 8) : below(mid, level, 8), "CHoCH", c, 10, "centerMiddle");
    }

    // Tramo de bloque entre x1 y x2 (relleno + bordes superior e inferior; borde izquierdo al nacer)
    drawBlock(ob, bull, x1, x2, first) {
        const c = bull ? BULL : BEAR;
        this.push({ tag: "Shapes", key: this.key(),
            primitives: [{ tag: "Rectangle", position: at(x1, ob.top), size: { width: du(x2 - x1), height: du(ob.bottom - ob.top) } }],   // altura negativa: hacia abajo
            fillStyle: fill(c, 0.16) });
        const lines = [
            { tag: "Line", a: at(x1, ob.top), b: at(x2, ob.top) },
            { tag: "Line", a: at(x1, ob.bottom), b: at(x2, ob.bottom) }
        ];
        if (first) lines.push({ tag: "Line", a: at(x1, ob.top), b: at(x1, ob.bottom) });
        this.push({ tag: "LineSegments", key: this.key(), lines, lineStyle: { lineWidth: 1, color: c } });
    }

    drawTradeSegment(t, x1, x2) {
        this.line(x1, t.entry, x2, t.entry, GRAY, 1);
        this.line(x1, t.stop, x2, t.stop, BEAR, 1);
        this.line(x1, t.target, x2, t.target, BULL, 1);
        if (num(t.tp1)) this.line(x1, t.tp1, x2, t.tp1, BULL, 1, 3);
    }

    markSignal(k, b, dir, stop, tp1) {
        const isLong = dir === 1;
        this.text(isLong ? below(k, b.l, 10) : above(k, b.h, 10), "OB", isLong ? BULL : BEAR, 10, "centerMiddle", "bold");
        const risk = Math.abs(b.c - stop);
        this.risk = { k, entry: b.c, stop, tp1, tp2: b.c + dir * risk * this.props.targetR };
    }

    // Proyección fija de stop y objetivos de la última señal (solo en la última barra)
    riskProjection(items) {
        const r = this.risk, x2 = r.k + this.props.projectionBars;
        const g = (key, obj) => items.push(Object.assign({ key: "risk_" + key, global: true }, obj));
        const seg = (y, color, width, dash) => ({ tag: "LineSegments", lines: [{ tag: "Line", a: at(r.k, y), b: at(x2, y) }],
            lineStyle: { lineWidth: width, color, lineStyle: dash || 1 } });
        const lbl = (y, txt, color) => ({ tag: "Text", point: rightOf(x2, y), text: txt, style: { fontSize: 10, fill: color }, textAlignment: "leftMiddle" });
        g("entry", seg(r.entry, GRAY, 1, 3));
        g("stop", seg(r.stop, BEAR, 2));
        g("stopTxt", lbl(r.stop, "Stop " + fmt(r.stop, 2), BEAR));
        g("tp2", seg(r.tp2, BULL, 2));
        g("tp2Txt", lbl(r.tp2, "TP2 " + fmt(r.tp2, 2), BULL));
        if (num(r.tp1)) {
            g("tp1", seg(r.tp1, BULL, 1, 3));
            g("tp1Txt", lbl(r.tp1, "TP1 " + fmt(r.tp1, 2), BULL));
        }
    }

    // Panel de estadísticas a la derecha de la última vela. Tradovate no ancla bien a las esquinas (origin), así que
    // va en coordenadas de la barra actual; global + clave fija = se dibuja una sola vez, con los últimos valores.
    statsPanel(items, i, price) {
        const p = this.props, st = this.st, t = this.trd;
        const hp = p.emaTrendFilter || p.minLegDisplacement;
        const rows = [
            ["Signals · " + (p.tradeEntry === "choch" ? "CHoCH + block" : "Block rejection") + (hp ? " · HP filter" : ""), "#DBDBDB"],
            ["BUY " + st.buy + "   SELL " + st.sell + "   Total " + (st.buy + st.sell), "#DBDBDB"],
            ["Closed " + st.n + (t ? (t.dir === 1 ? " + LONG" : " + SHORT") : "") + "   Win " + (st.n ? (100 * st.won / st.n).toFixed(1) + "%" : "—")
                + "   Net R " + signed(st.r, 1), st.r >= 0 ? BULL : BEAR],
            ["TP " + st.tp + "   BE " + st.be + "   SL " + st.sl, "#DBDBDB"]
        ];
        const x = op(du(i), "+", px(p.statsOffsetPx + 150));
        rows.forEach((r, n) => items.push({ tag: "Text", key: "stats_" + n, global: true,
            point: { x, y: op(du(price), "+", px(40 + n * 18)) }, text: r[0],
            style: { fontSize: 12, fill: r[1], fontWeight: n === 2 ? "bold" : "normal" }, textAlignment: "centerMiddle" }));
    }

    // --- VWAP anclado ---
    vwapCalc(st, b, prev) {
        const p = this.props;
        const anchor = p.vwapResetAt;
        const periodAnchor = anchor === "day" || anchor === "week" || anchor === "month";
        const sess = { ny: ["0930-1600", "America/New_York"], london: ["0800-1630", "Europe/London"], tokyo: ["0900-1530", "Asia/Tokyo"],
            sydney: ["1000-1600", "Australia/Sydney"], globex: ["1800-1700", "America/New_York"] }[anchor] || [p.vwapCustomSession, p.vwapCustomTz];
        const session = parseSession(sess[0], [570, 960]);
        const tz = sess[1];
        let inSess = false, reset;
        if (periodAnchor) {
            const keyOf = x => anchor === "day" ? x.td : anchor === "month" ? Math.floor(x.td / 100) : weekKey(x.td);
            reset = !prev || keyOf(b) !== keyOf(prev);
        }
        else if (this.intraday) {
            inSess = inSessionMin(localMinute(b.t, tz), session);
            // La apertura de la sesión cae entre el cierre de la barra previa y el de esta (símbolos sin barra fuera de sesión)
            let crossed = false;
            if (prev && this.barMs) {
                const prevClose = prev.t + this.barMs, curClose = b.t + this.barMs;
                if (curClose - prevClose >= 86400000) crossed = true;
                else {
                    const a = localMinute(prevClose, tz), c = localMinute(curClose, tz), o = session[0];
                    crossed = a <= c ? o >= a && o < c : o >= a || o < c;
                }
            }
            reset = inSess && (!st.inSess || crossed);
        }
        else reset = !prev || b.td !== prev.td;

        const src = p.vwapSource === "close" ? b.c : p.vwapSource === "hl2" ? (b.h + b.l) / 2 : p.vwapSource === "ohlc4" ? (b.o + b.h + b.l + b.c) / 4 : (b.h + b.l + b.c) / 3;
        const base = reset ? { sv: 0, svs: 0, svs2: 0 } : st;
        const next = { sv: base.sv + b.v, svs: base.svs + b.v * src, svs2: base.svs2 + b.v * src * src, inSess };
        const value = next.sv > 0 ? next.svs / next.sv : NaN;
        const stdev = next.sv > 0 ? Math.sqrt(Math.max(next.svs2 / next.sv - value * value, 0)) : NaN;
        const visible = p.showVwap && (!p.vwapOnlyInSession || !this.intraday || periodAnchor || inSess);
        return { next, value, stdev, reset, visible };
    }

    bands(vr) {
        const p = this.props, v = vr.value;
        const off = m => p.vwapBands === "percent" ? v * m / 100 : vr.stdev * m;
        const o1 = off(p.band1Mult), o2 = off(p.band2Mult), o3 = off(p.band3Mult);
        // Borde interior de las bandas 2 y 3: la banda activa anterior o el VWAP
        const in2 = p.band1 ? o1 : 0, in3 = p.band2 ? o2 : in2;
        return { vwap: v, u1: v + o1, l1: v - o1, u2: v + o2, l2: v - o2, u3: v + o3, l3: v - o3,
            in2u: v + in2, in2l: v - in2, in3u: v + in3, in3l: v - in3 };
    }

    // Relleno entre bandas de la barra previa a la actual
    bandFills(items, i, a, z) {
        const p = this.props;
        const alpha = Math.max(0, Math.min(1, (100 - p.fillTransparency) / 100));
        const quad = (k1, k2, color) => items.push({ tag: "Shapes", key: "fill_" + i + "_" + k1 + k2, global: true, fillStyle: fill(color, alpha),
            primitives: [{ tag: "Polygon", points: [at(i - 1, a[k1]), at(i, z[k1]), at(i, z[k2]), at(i - 1, a[k2])] }] });
        if (p.band1) quad("u1", "l1", "#089981");
        if (p.band2) { quad("u2", "in2u", "#808000"); quad("in2l", "l2", "#808000"); }
        if (p.band3) { quad("u3", "in3u", "#00897b"); quad("in3l", "l3", "#00897b"); }
    }
}

function rightOf(x, y) { return { x: op(du(x), "+", px(4)), y: du(y) }; }

// Lunes de la semana de un día aaaammdd
function weekKey(td) {
    const y = Math.floor(td / 10000), m = Math.floor(td / 100) % 100, d = td % 100;
    const t = Date.UTC(y, m - 1, d);
    const dow = (new Date(t).getUTCDay() + 6) % 7;
    return Math.floor((t - dow * 86400000) / 86400000);
}

module.exports = {
    name: "stObRejections",
    description: "ST OB Rejections",
    calculator: stObRejections,
    inputType: meta.InputType.BARS,
    areaChoice: meta.AreaChoice.OVERLAY,
    tags: ["SMC"],
    params: {
        // Bloques
        pivotBars: predef.paramSpecs.number(3, 1, 1),
        searchBeforeOrigin: predef.paramSpecs.number(8, 1, 1),
        maxLegBars: predef.paramSpecs.number(100, 1, 10),
        requireDisplacement: predef.paramSpecs.bool(true),
        reactionWindow: predef.paramSpecs.number(8, 1, 1),
        zoneInteraction: predef.paramSpecs.enum({ touch: "Touch", midpoint: "Midpoint", sweep: "Sweep" }, "touch"),
        minBodyPercent: predef.paramSpecs.number(0, 1, 0),
        trackedBlocks: predef.paramSpecs.number(100, 1, 1),
        // Estructura
        showChoch: predef.paramSpecs.bool(true),
        onlyWithStructure: predef.paramSpecs.bool(true),
        requireSwingRange: predef.paramSpecs.bool(true),
        avoidFreshExtremes: predef.paramSpecs.bool(true),
        requirePivotRange: predef.paramSpecs.bool(false),
        rangeAtrLength: predef.paramSpecs.number(14, 1, 1),
        minPivotRangeAtr: predef.paramSpecs.number(2.0, 0.1, 0.1),
        // Sesión (hora de Nueva York)
        signalsOnlyInSession: predef.paramSpecs.bool(true),
        signalSession: predef.paramSpecs.text("0930-1600"),
        // Limpieza
        suppressOverlapping: predef.paramSpecs.bool(true),
        overlapThreshold: predef.paramSpecs.number(25, 1, 1),
        // Riesgo
        projectRisk: predef.paramSpecs.bool(false),
        stopBufferTicks: predef.paramSpecs.number(2, 1, 0),
        targetR: predef.paramSpecs.number(2.0, 0.5, 0.5),
        projectionBars: predef.paramSpecs.number(20, 1, 5),
        // Trades
        tradeSignals: predef.paramSpecs.bool(true),
        tradeEntry: predef.paramSpecs.enum({ choch: "CHoCH + block", rejection: "Block rejection" }, "choch"),
        tradeTarget: predef.paramSpecs.enum({ r: "R multiple", leg: "Leg extreme (TP1)" }, "r"),
        maxBarsInTrade: predef.paramSpecs.number(0, 1, 0),
        tp1R: predef.paramSpecs.number(0.5, 0.1, 0.1),
        tp1PartialPct: predef.paramSpecs.number(50, 5, 0),
        beAfterTp1: predef.paramSpecs.bool(true),
        exitOnNewChoch: predef.paramSpecs.bool(false),
        tradesOnlyInSession: predef.paramSpecs.bool(false),
        showStats: predef.paramSpecs.bool(true),
        statsOffsetPx: predef.paramSpecs.number(40, 10, 0),
        // Filtro de alta probabilidad
        emaTrendFilter: predef.paramSpecs.bool(true),
        fastEma: predef.paramSpecs.number(50, 1, 2),
        fastSlopeBars: predef.paramSpecs.number(10, 1, 1),
        slowEma: predef.paramSpecs.number(200, 1, 2),
        slowSlopeBars: predef.paramSpecs.number(20, 1, 1),
        minLegDisplacement: predef.paramSpecs.bool(true),
        minLegAtr: predef.paramSpecs.number(2.5, 0.1, 0),
        showTrendEmas: predef.paramSpecs.bool(false),
        // VWAP
        showVwap: predef.paramSpecs.bool(true),
        vwapSource: predef.paramSpecs.enum({ hlc3: "HLC3", close: "Close", hl2: "HL2", ohlc4: "OHLC4" }, "hlc3"),
        vwapResetAt: predef.paramSpecs.enum({ ny: "New York", london: "London", tokyo: "Tokyo", sydney: "Sydney", globex: "CME Globex (18:00 NY)",
            day: "Day", week: "Week", month: "Month", custom: "Custom session" }, "ny"),
        vwapCustomSession: predef.paramSpecs.text("0930-1600"),
        vwapCustomTz: predef.paramSpecs.enum({ "America/New_York": "New York", "America/Chicago": "Chicago", "America/Los_Angeles": "Los Angeles",
            "America/Sao_Paulo": "Sao Paulo", "Europe/London": "London", "Europe/Berlin": "Berlin", "Asia/Tokyo": "Tokyo", "Asia/Hong_Kong": "Hong Kong",
            "Asia/Singapore": "Singapore", "Australia/Sydney": "Sydney", "UTC": "UTC" }, "America/New_York"),
        vwapOnlyInSession: predef.paramSpecs.bool(false),
        vwapBands: predef.paramSpecs.enum({ stdev: "Standard deviation", percent: "Percentage" }, "stdev"),
        band1: predef.paramSpecs.bool(true),
        band1Mult: predef.paramSpecs.number(1.0, 0.5, 0),
        band2: predef.paramSpecs.bool(true),
        band2Mult: predef.paramSpecs.number(2.0, 0.5, 0),
        band3: predef.paramSpecs.bool(false),
        band3Mult: predef.paramSpecs.number(3.0, 0.5, 0),
        fillBands: predef.paramSpecs.bool(true),
        fillTransparency: predef.paramSpecs.number(92, 1, 0)
    },
    plots: {
        emaFast: { title: "Trend EMA fast" },
        emaSlow: { title: "Trend EMA slow" },
        vwap: { title: "VWAP" },
        upper1: { title: "VWAP upper 1" },
        lower1: { title: "VWAP lower 1" },
        upper2: { title: "VWAP upper 2" },
        lower2: { title: "VWAP lower 2" },
        upper3: { title: "VWAP upper 3" },
        lower3: { title: "VWAP lower 3" }
    },
    schemeStyles: {
        dark: {
            emaFast: { color: "#ff9800", opacity: 80 },
            emaSlow: { color: "#9c27b0", lineWidth: 2, opacity: 80 },
            vwap: { color: "#2962ff", lineWidth: 2, opacity: 100 },
            upper1: { color: "#089981", opacity: 100 }, lower1: { color: "#089981", opacity: 100 },
            upper2: { color: "#808000", opacity: 100 }, lower2: { color: "#808000", opacity: 100 },
            upper3: { color: "#00897b", opacity: 100 }, lower3: { color: "#00897b", opacity: 100 }
        },
        light: {
            emaFast: { color: "#ff9800", opacity: 80 },
            emaSlow: { color: "#9c27b0", lineWidth: 2, opacity: 80 },
            vwap: { color: "#2962ff", lineWidth: 2, opacity: 100 },
            upper1: { color: "#089981", opacity: 100 }, lower1: { color: "#089981", opacity: 100 },
            upper2: { color: "#808000", opacity: 100 }, lower2: { color: "#808000", opacity: 100 },
            upper3: { color: "#00897b", opacity: 100 }, lower3: { color: "#00897b", opacity: 100 }
        }
    }
};
