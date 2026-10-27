// Oliver Velez – Patrones: medias móviles 8 / 20 / 200, VWAP y patrones de vela del manual de Oliver Velez (iFundTraders),
// marcados al cierre de cada vela con el color del patrón y su sigla SOLO cuando otra metodología o la validación de 3
// temporalidades los confirma. Definiciones base (cada número es configurable):
//   - EB  Elephant (WRB, cap. 1): cuerpo >= 1,5 x el cuerpo medio de las 20 velas anteriores, cuerpo >= 60 % del rango y
//         cierre en el tercio de su dirección.
//   - +180 / -180 (cap. 17.1, versión clásica de 2 velas): vela amplia roja (verde) que cierra en su cuarto inferior
//         (superior), seguida de una vela amplia verde (roja) que cierra en su cuarto superior (inferior) recuperando al
//         menos 2/3 del rango de la primera. Amplia = rango >= 1 x el rango medio de 20 velas y cuerpo >= 50 % del rango.
//   - Señales de reversa (cap. 11, págs. 189-204), tras una vela que hace máximo y mínimo más bajos (piso) o más altos (techo)
//     que la anterior:
//       BT / TT   cola de piso / techo de al menos el 60 % del rango (las del manual miden 65-67 %).
//       GBR / RBR vela verde (roja) tras una roja (verde) que recupera al menos el 75 % de su cuerpo (manual: 83-92 %).
//       NRB       rango <= 50 % del rango medio de 20 velas (de piso tras caída, de techo tras rally).
//       NB        cuerpo <= 25 % del cuerpo medio de 20 velas, verde o roja (de piso tras caída, de techo tras rally).
//   - RBI / GBI (cap. 17.2, págs. 287-299): vela verde (roja) sólida, luego una vela roja (verde) en su tercio superior
//     (inferior) sin superarla; cuando la vela 3 o 4 opera por encima (debajo) del máximo (mínimo) de la vela contraria sin
//     haber perdido su mínimo (máximo), se marca la vela ignorada. Solo en tendencia: la 20mm sube (baja) y la vela ignorada
//     queda sobre (bajo) ella.
//   - +BMG / -BMG (cap. 18, págs. 300-319): la sesión RTH anterior (09:30-16:00 NY) fue una vela roja (verde) sólida, cuerpo
//     >= 60 % del rango, y la sesión de hoy abre sobre su máximo (bajo su mínimo); se marca la primera vela RTH.
//   - VBS / VSS, Setup 1 (cap. 10.1, págs. 128-145): retroceso de 3+ velas escalonadas o del color contrario con la 20mm a
//     favor; flecha y sigla en la vela que opera sobre el máximo (bajo el mínimo) de la anterior.
//   - Regalo (cap. 10.2, págs. 146-154): el rango de cada Elephant es una zona; una vela que borra el 100 % de la vela
//     contraria anterior dentro de la zona es la entrada (REG).
// Confirmación (estudio "OV Patrones Estudio", MNQ 2 min 10/2025-10/2026 validado en MES): para cada patrón, el filtro que más
// sube el % de señales que llegan a 1R antes que al stop (entrada Velez 1 tick tras la vela, stop en el otro extremo), con la
// mejora mínima de MNQ 1.ª mitad, MNQ 2.ª mitad y MES y al menos 80 señales en cada una:
//   EB       sin filtro: se dibujan todas (elección del usuario). El filtro medido (subasta fallida de Market Profile: la vela o la
//            anterior barre el VAL (VAH) RTH de ayer y cierra dentro, y ATR alto) solo marca EbAlcPrem / EbBajPrem para las estrategias
//   180      volumen menor que el de las 2 velas anteriores (VSA) y ATR alto
//   BT / TT  tras un movimiento fuerte en contra (eficiencia de 20 velas <= -0,3, Brooks) y barrido de liquidez (ICT)
//   GBR/RBR  20mm de 15 y 60 min con pendiente a favor (3 temporalidades) y barrido de liquidez (ICT)
//   NRB      cierre en el 30 % a favor de la vela (Brooks) y barrido de liquidez (ICT)
//   NB       cierre en el 30 % a favor de la vela (Brooks) y kill zone de Nueva York 09:30-11:30 (ICT)
//   VBS/VSS  15 y 60 min con el cierre del lado de su 20mm y la 20mm a favor (3 temporalidades) y cierre en el 30 % a favor
//   REG      3 velas de caída / rally (Velez) y volumen menor que el de las 2 velas anteriores (VSA)
//   RBI/GBI, BMG  3 temporalidades como VBS (demasiado raros para medirlos: 17 y 11 al año)
// Las señales de reversa (180, BT / TT, GBR / RBR, NRB, NB) también se muestran si la mecha toca el VWAP o una banda visible y la vela
// cierra del lado de la reversa (petición del usuario; en el estudio con el VWAP semanal acertaron ~48 % a 1R, como sin filtro).
// Barrido de liquidez: la vela o la anterior pasa un nivel y la vela cierra de vuelta: mínimo (máximo) RTH de ayer, overnight
// (en RTH), Asia 18:00-02:00, Londres 02:00-05:00, Initial Balance 09:30-10:30, VAL (VAH) de ayer o el último pivote de 60 velas.
// ATR alto: ATR(14) >= 1,2 x su media de 390 velas. Mejora del % a 1R: de +2 a +7 puntos; solo EB tuvo resultado neto positivo
// tras comisión en MNQ (MES en equilibrio). Las señales no confirmadas no se dibujan.
// Si una vela cumple varios patrones confirmados toma el color del más fuerte (BMG > 180 > EB > BT/TT > GBR/RBR > RBI/GBI > NRB
// > NB) y se escriben todas sus siglas: las alcistas debajo de la vela, las bajistas encima.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
#endregion

public enum OvTipoMedia { SMA, EMA, WMA, HMA, SMMA, TMA, TEMA, DEMA, VWMA, ZLEMA, LinReg, KAMA, T3 }

// Zona de Regalo de "Oliver Velez Patrones": rango de una Elephant hasta la entrada o la invalidación
public class OvZona
{
	public int Idx, Fin = -1, Dir;   // vela de la Elephant, vela de cierre de la zona (-1 = activa), 1 alcista / -1 bajista
	public double Alto, Bajo;
	public bool Confirmada;          // terminó en una entrada REG confirmada: solo esas zonas se dibujan
}

// Máximo o mínimo anterior (pivote) de "Oliver Velez Patrones"
public class OvPivote
{
	public int Idx, Fin = -1;   // vela del pivote y vela que lo rompe al cierre (-1 = vigente)
	public double Precio;
	public bool Alto;
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class OliverVelezPatrones : Indicator
	{
		// Patrones por vela (bits); públicos para las estrategias que leen PatronesVela
		public const int EbAlc = 1, EbBaj = 2, Toro180 = 4, Oso180 = 8, Bt = 16, Tt = 32, Gbr = 64, Rbr = 128,
			Rbi = 256, Gbi = 512, NrbPiso = 1024, NrbTecho = 2048, NbPiso = 4096, NbTecho = 8192,
			BmgToro = 16384, BmgOso = 32768, EbAlcPrem = 65536, EbBajPrem = 131072, SetupConf = 1 << 20;
		// EbAlcPrem / EbBajPrem = EB que pasa el filtro (subasta fallida + ATR alto), en PatronesVela; ConfirmadosVela devuelve los bits de los
		// patrones confirmados de la vela (y SetupConf si su setup VBS / VSS / REG está confirmado)

		private Series<int> patrones, confirmados;
		private readonly ISeries<double>[] medias = new ISeries<double>[3];
		private readonly Series<double>[] smma = new Series<double>[3];
		private ATR atr;
		private SMA volSma;

		// Ítems de ubicación: pivotes confirmados (máximos y mínimos anteriores) y cierre del día anterior
		private readonly List<OvPivote> pivotes = new List<OvPivote>();
		private TimeZoneInfo nyTz;
		private DateTime etPrev = DateTime.MinValue;
		private double pdc = double.NaN, ubicExt;
		// Sesión RTH en curso y anterior (para el gap monstruo) y gap de esta vela: 1 Toro, -1 Oso, 0 ninguno
		private double sesO, sesH, sesL, sesAntO, sesAntH, sesAntL, sesAntC;
		private bool sesActiva, sesAntValida;
		private int bmgBar;
		private Series<double> pdcSerie;
		// Setups por vela: 1 VBS, 2 VBS con calidad, 3 compra de regalo, 4 regalo con calidad; negativos = venta
		private Series<int> setups;
		private readonly List<OvZona> zonas = new List<OvZona>();
		private int ultimaVbs = -1, ultimaVss = -1;
		// VWAP con bandas (motor compartido AddOnsVwapEngine.cs)
		private VwapEngine vwapEng;
		private DateTime vwapPrevEt;
		private bool vwapCongelado, vwapBandasVistas;
		// Confirmación: perfil de volumen de la sesión RTH en curso, área de valor de la anterior, ATR medio, 20mm de 15 y 60 min y
		// niveles de liquidez de la sesión (overnight, Asia, Londres, Initial Balance)
		private SMA atrLargo, sma15, sma60;
		private readonly Dictionary<int, double> perfil = new Dictionary<int, double>();
		private double vah = double.NaN, val = double.NaN;
		private Series<double> vahSerie, valSerie;
		private DateTime etSes = DateTime.MinValue;
		private double onH = double.NaN, onL = double.NaN, onHf = double.NaN, onLf = double.NaN, asH = double.NaN, asL = double.NaN, asHf = double.NaN,
			asLf = double.NaN, lnH = double.NaN, lnL = double.NaN, lnHf = double.NaN, lnLf = double.NaN, ibH = double.NaN, ibL = double.NaN;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description              = "Medias móviles 8 / 20 / 200, VWAP y patrones de vela de Oliver Velez (Elephant, 180, BT/TT, GBR/RBR, NRB, NB, RBI/GBI, BMG, Setup 1, Regalo) marcados al cierre de la vela. Solo se dibujan las señales confirmadas por otra metodología (Brooks, VSA, ICT, Market Profile) o por 3 temporalidades (2, 15 y 60 min).";
				Name                     = "Oliver Velez Patrones";
				Calculate                = Calculate.OnBarClose;   // los patrones se confirman al cierre: nunca cambian después
				IsOverlay                = true;
				DisplayInDataBox         = true;
				DrawOnPricePanel         = true;
				PaintPriceMarkers        = false;
				IsSuspendedWhileInactive = true;
				MaximumBarsLookBack      = MaximumBarsLookBack.Infinite;

				// 1. Medias móviles
				Ma1Tipo = OvTipoMedia.SMA; Ma1Periodo = 8;
				Ma2Tipo = OvTipoMedia.SMA; Ma2Periodo = 20;
				Ma3Tipo = OvTipoMedia.SMA; Ma3Periodo = 200;
				VerMedia1 = VerMedia2 = VerMedia3 = true;

				// 2. Patrones visibles
				VerElephant = VerP180 = VerColas = VerReversa = VerIgnorada = VerNrb = VerNb = true;
				PintarVelas  = true;
				VerSiglas    = true;
				VerLeyenda   = true;
				TamanoSigla  = 10;

				// 3. Definiciones
				Promedio          = 20;
				EbMultiplicador   = 1.5;
				EbCuerpoMin       = 0.6;
				EbZonaCierre      = 1.0 / 3.0;
				P180AnchoMult     = 1.0;
				P180CuerpoMin     = 0.5;
				P180ZonaCierre    = 0.25;
				P180Retroceso     = 2.0 / 3.0;
				IgnPorCierre      = false;
				IgnCuerpoV1       = 0.5;
				IgnTercio         = true;
				IgnZona           = 1.0 / 3.0;
				IgnTendencia      = true;
				ColaMin           = 0.6;
				ReversaMin        = 0.75;
				// 5. Ubicación
				DistUbicAtr       = 0.5;
				UbicMedias        = true;
				Pendiente20mm     = 3;
				Ubic200           = true;
				Plana200Atr       = 0.5;
				Plana200Velas     = 20;
				UbicPivotes       = true;
				PivoteVelas       = 5;
				UbicPdc           = true;
				UbicRetroceso     = true;
				RetrocesoMin      = 0.33;
				RetrocesoMax      = 0.55;
				UbicExtension     = true;
				ExtensionAtr      = 3.0;
				VerPdc            = true;
				VerPivotes        = true;
				PivotesVigentes   = 3;
				ColorPdc          = MakeBrush(0x75, 0x75, 0x75);   // gris
				ColorPivoteAlto   = MakeBrush(0xE5, 0x39, 0x35);   // rojo
				ColorPivoteBajo   = MakeBrush(0x43, 0xA0, 0x47);   // verde
				NrbMax            = 0.5;
				NbMax             = 0.25;

				// 4. Paleta
				ColorEbAlc    = MakeBrush(0x00, 0xC8, 0x53);   // verde
				ColorEbBaj    = MakeBrush(0xD5, 0x00, 0x00);   // rojo
				ColorToro180  = MakeBrush(0x29, 0x62, 0xFF);   // azul
				ColorOso180   = MakeBrush(0xAA, 0x00, 0xFF);   // violeta
				ColorBt       = MakeBrush(0xFF, 0xAB, 0x00);   // ámbar
				ColorTt       = MakeBrush(0xFF, 0x6D, 0x00);   // naranja
				ColorGbr      = MakeBrush(0x00, 0xBF, 0xA5);   // turquesa
				ColorRbr      = MakeBrush(0xF5, 0x00, 0x57);   // rosa
				ColorRbi      = MakeBrush(0x64, 0xDD, 0x17);   // lima
				ColorGbi      = MakeBrush(0x8D, 0x6E, 0x63);   // café
				ColorNrbPiso  = MakeBrush(0x00, 0xB8, 0xD4);   // cian
				ColorNrbTecho = MakeBrush(0xEC, 0x40, 0x7A);   // frambuesa
				ColorNbPiso   = MakeBrush(0x54, 0x6E, 0x7A);   // gris azulado
				ColorNbTecho  = MakeBrush(0x9E, 0x9E, 0x9E);   // gris
				ColorBmgToro  = MakeBrush(0x00, 0x91, 0xEA);   // azul brillante
				ColorBmgOso   = MakeBrush(0xC5, 0x11, 0x62);   // magenta oscuro
				VerMonstruo       = true;
				MonstruoCuerpoMin = 0.6;
				VerVbs            = true;
				VelasSetup        = 3;
				VerRegalo         = true;
				OpacidadRegalo    = 12;
				CalidadUbicacion  = true;
				CalidadHora       = true;
				HoraTolerancia    = 5;
				CalidadVolumen    = true;
				VolumenSurge      = 1.5;
				ColorVbs          = MakeBrush(0x00, 0xA8, 0x4F);   // verde esmeralda
				ColorVss          = MakeBrush(0xE5, 0x1C, 0x23);   // rojo vivo
				ColorRegaloAlc    = MakeBrush(0x26, 0xA6, 0x9A);   // verde azulado
				ColorRegaloBaj    = MakeBrush(0xEF, 0x6C, 0x00);   // naranja quemado

				// 7. VWAP
				VerVwap            = true;
				VwapAnclaSel       = VwapAncla.RTH;
				VwapHoraAncla      = 930;
				VwapFinVentana     = 1600;
				VwapHoraRollEth    = 1800;
				VwapFuente         = VwapPrecio.HLC3;
				VwapBanda1 = VwapBanda2 = VwapBanda3 = true;
				VwapMult1          = 1.0;
				VwapMult2          = 2.0;
				VwapMult3          = 3.0;
				VwapOcultarMin     = 10;
				VwapEtiquetas      = true;
				VwapTamanoEtiqueta = 12;
				VerVwapCentral     = true;
				EscalaSoloVelas    = true;
				UbicVwap           = true;

				// 8. Confirmación
				PremiumAtrMin      = 1.2;
				PremiumAtrVelas    = 390;
				ReversaEnBandaVwap = true;
				PerfilTicks        = 4;
				VerAreaValor       = true;
				ColorAreaValor     = MakeBrush(0x5C, 0x6B, 0xC0);   // azul pizarra

				AddPlot(new Stroke(MakeBrush(0xFF, 0xA7, 0x26), 2), PlotStyle.Line, "Media 1");
				AddPlot(new Stroke(MakeBrush(0x29, 0x79, 0xFF), 2), PlotStyle.Line, "Media 2");
				AddPlot(new Stroke(MakeBrush(0xC6, 0x28, 0x28), 3), PlotStyle.Line, "Media 3");
				AddPlot(new Stroke(MakeBrush(0x8E, 0x24, 0xAA), 2), PlotStyle.Line, "VWAP");
				AddPlot(new Stroke(MakeBrush(0x00, 0x89, 0x7B), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP +1");
				AddPlot(new Stroke(MakeBrush(0x00, 0x89, 0x7B), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP -1");
				AddPlot(new Stroke(MakeBrush(0xF5, 0x7C, 0x00), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP +2");
				AddPlot(new Stroke(MakeBrush(0xF5, 0x7C, 0x00), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP -2");
				AddPlot(new Stroke(MakeBrush(0xD3, 0x2F, 0x2F), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP +3");
				AddPlot(new Stroke(MakeBrush(0xD3, 0x2F, 0x2F), DashStyleHelper.Dash, 1), PlotStyle.Line, "VWAP -3");
			}
			else if (State == State.Configure)
			{
				// 3 temporalidades: la 20mm de 15 y 60 min (una estrategia que aloje el indicador debe añadir estas mismas series)
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Minute, 60);
			}
			else if (State == State.DataLoaded)
			{
				patrones = new Series<int>(this, MaximumBarsLookBack.Infinite);
				confirmados = new Series<int>(this, MaximumBarsLookBack.Infinite);
				sma15    = SMA(BarsArray[1], 20);
				sma60    = SMA(BarsArray[2], 20);
				atr      = ATR(14);
				volSma   = SMA(Volume, 20);
				pdcSerie = new Series<double>(this, MaximumBarsLookBack.Infinite);
				setups   = new Series<int>(this, MaximumBarsLookBack.Infinite);
				zonas.Clear();
				ultimaVbs = ultimaVss = -1;
				vwapEng = new VwapEngine();
				vwapPrevEt = DateTime.MinValue;
				vwapCongelado = vwapBandasVistas = false;
				atrLargo = SMA(atr, PremiumAtrVelas);
				vahSerie = new Series<double>(this, MaximumBarsLookBack.Infinite);
				valSerie = new Series<double>(this, MaximumBarsLookBack.Infinite);
				perfil.Clear();
				vah = val = double.NaN;
				etSes = DateTime.MinValue;
				onH = onL = onHf = onLf = asH = asL = asHf = asLf = lnH = lnL = lnHf = lnLf = ibH = ibL = double.NaN;
				pivotes.Clear();
				etPrev = DateTime.MinValue;
				pdc = double.NaN;
				try { nyTz = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
				catch { nyTz = null; }
				OvTipoMedia[] tipos = { Ma1Tipo, Ma2Tipo, Ma3Tipo };
				int[] periodos = { Ma1Periodo, Ma2Periodo, Ma3Periodo };
				for (int i = 0; i < 3; i++)
				{
					smma[i]   = tipos[i] == OvTipoMedia.SMMA ? new Series<double>(this, MaximumBarsLookBack.Infinite) : null;
					medias[i] = CrearMedia(tipos[i], periodos[i]);
				}
			}
		}

		private ISeries<double> CrearMedia(OvTipoMedia tipo, int p)
		{
			switch (tipo)
			{
				case OvTipoMedia.EMA:    return EMA(Input, p);
				case OvTipoMedia.WMA:    return WMA(Input, p);
				case OvTipoMedia.HMA:    return HMA(Input, Math.Max(2, p));
				case OvTipoMedia.TMA:    return TMA(Input, p);
				case OvTipoMedia.TEMA:   return TEMA(Input, p);
				case OvTipoMedia.DEMA:   return DEMA(Input, p);
				case OvTipoMedia.VWMA:   return VWMA(Input, p);
				case OvTipoMedia.ZLEMA:  return ZLEMA(Input, p);
				case OvTipoMedia.LinReg: return LinReg(Input, Math.Max(2, p));
				case OvTipoMedia.KAMA:   return KAMA(Input, 2, p, 30);
				case OvTipoMedia.T3:     return T3(Input, p, 3, 0.7);
				case OvTipoMedia.SMMA:   return null;   // calculada aquí (Wilder / RMA)
				default:                 return SMA(Input, p);
			}
		}

		protected override void OnBarUpdate()
		{
			// Las series de 15 y 60 min solo alimentan su 20mm; todo se calcula al cierre de la vela del gráfico
			if (BarsInProgress != 0)
				return;
			// --- Medias móviles ---
			int[] periodos = { Ma1Periodo, Ma2Periodo, Ma3Periodo };
			for (int i = 0; i < 3; i++)
			{
				double v;
				if (smma[i] != null)
				{
					int p = periodos[i];
					v = CurrentBar == 0 ? Input[0] : (smma[i][1] * (p - 1) + Input[0]) / p;
					smma[i][0] = v;
				}
				else
					v = medias[i][0];
				if (CurrentBar >= periodos[i] - 1)
				{
					Values[i][0] = v;
					if (!LineaVisible(i))
						PlotBrushes[i][0] = Brushes.Transparent;   // oculta: se sigue calculando para la ubicación y los setups
				}
			}

			ActualizarVwap();
			ActualizarSesiones();
			ActualizarUbicaciones();

			// --- Patrones, al cierre de la vela ---
			patrones[0] = 0;
			confirmados[0] = 0;
			int n = Promedio;
			if (CurrentBar < Math.Max(n + 5, 25))
				return;

			double rango = High[0] - Low[0], cuerpo = Math.Abs(Close[0] - Open[0]);
			double cuerpoMedio = 0, rangoMedio = 0, rangoMedio180 = 0;
			for (int i = 1; i <= n; i++)
			{
				cuerpoMedio += Math.Abs(Close[i] - Open[i]);
				rangoMedio  += High[i] - Low[i];
				rangoMedio180 += High[i + 1] - Low[i + 1];   // sin las dos velas del 180
			}
			cuerpoMedio /= n; rangoMedio /= n; rangoMedio180 /= n;
			bool verde = Close[0] > Open[0], roja = Close[0] < Open[0];
			int f = 0;

			// Elephant
			if (VerElephant && rango > 0 && cuerpo >= EbMultiplicador * cuerpoMedio && cuerpo >= EbCuerpoMin * rango)
			{
				if (verde && Close[0] >= High[0] - EbZonaCierre * rango) f |= EbAlc;
				else if (roja && Close[0] <= Low[0] + EbZonaCierre * rango) f |= EbBaj;
			}

			// 180 clásico de 2 velas (sin exigir serie previa ni ubicación: la confirmación decide)
			if (VerP180 && rango > 0)
			{
				double r1 = High[1] - Low[1], b1 = Math.Abs(Close[1] - Open[1]);
				bool amplias = r1 > 0 && r1 >= P180AnchoMult * rangoMedio180 && rango >= P180AnchoMult * rangoMedio180
					&& b1 >= P180CuerpoMin * r1 && cuerpo >= P180CuerpoMin * rango;
				// Cap. 17.1: la 2ª vela recupera al menos 2/3 del rango de la 1ª (casos C, B y A de las págs. 276 y 282)
				bool toro = amplias && Close[1] < Open[1] && Close[1] <= Low[1] + P180ZonaCierre * r1
					&& verde && Close[0] >= High[0] - P180ZonaCierre * rango && Close[0] >= Low[1] + P180Retroceso * r1;
				bool oso = amplias && Close[1] > Open[1] && Close[1] >= High[1] - P180ZonaCierre * r1
					&& roja && Close[0] <= Low[0] + P180ZonaCierre * rango && Close[0] <= High[1] - P180Retroceso * r1;
				if (toro) f |= Toro180;
				else if (oso) f |= Oso180;
			}

			// Señales de reversa tras una vela que hace máximo y mínimo más bajos (piso) o más altos (techo) que la anterior
			bool caida = High[1] < High[2] && Low[1] < Low[2], rally = High[1] > High[2] && Low[1] > Low[2];
			if (caida || rally)
			{
				double colaInf = Math.Min(Open[0], Close[0]) - Low[0], colaSup = High[0] - Math.Max(Open[0], Close[0]);
				if (VerColas && rango > 0)
				{
					if (caida && colaInf >= ColaMin * rango) f |= Bt;
					if (rally && colaSup >= ColaMin * rango) f |= Tt;
				}
				if (VerReversa)
				{
					if (caida && verde && Close[1] < Open[1] && Close[0] >= Close[1] + ReversaMin * (Open[1] - Close[1])) f |= Gbr;
					if (rally && roja && Close[1] > Open[1] && Close[0] <= Close[1] - ReversaMin * (Close[1] - Open[1])) f |= Rbr;
				}
				if (VerNrb && rango <= NrbMax * rangoMedio)
					f |= caida ? NrbPiso : NrbTecho;
				if (VerNb && cuerpo <= NbMax * cuerpoMedio)
					f |= caida ? NbPiso : NbTecho;
			}
			if (VerMonstruo && bmgBar != 0)
				f |= bmgBar == 1 ? BmgToro : BmgOso;

			// --- Confirmación de cada patrón de esta vela ---
			int c = 0;
			int[] bits = { EbAlc, EbBaj, Toro180, Oso180, Bt, Tt, Gbr, Rbr, NrbPiso, NrbTecho, NbPiso, NbTecho, BmgToro, BmgOso };
			foreach (int b in bits)
				if ((f & b) != 0 && Confirma(b, Direccion(b)))
					c |= b;
			// EB que pasa el filtro (subasta fallida + ATR alto) en PatronesVela, para la estrategia "OV EB Premium"; en el gráfico se
			// dibujan todas las Elephant, sin filtro (elección del usuario)
			if ((c & EbAlc) != 0) f |= EbAlcPrem;
			if ((c & EbBaj) != 0) f |= EbBajPrem;
			c |= f & (EbAlc | EbBaj);
			patrones[0] = f;
			setups[0] = 0;
			if (VerVbs)
			{
				if (!Setup1(1, f)) Setup1(-1, f);
				int st = setups[0];
				// VBS / VSS: 3 temporalidades (cierre del lado de la 20mm y 20mm a favor en 15 y 60 min) y cierre en el 30 % a favor
				if (st != 0 && TresTemporalidades(Math.Sign(st)) && Clv(Math.Sign(st)) >= 0.7)
					c |= SetupConf;
			}
			if (VerRegalo && Regalo(f))
				c |= SetupConf;
			confirmados[0] = c;
			Pintar(0);

			// RBI / GBI: se marca la vela ignorada (1 o 2 velas atrás) cuando esta vela confirma la ruptura
			if (VerIgnorada)
				for (int k = 1; k <= 2; k++)
				{
					if (MarcarIgnorada(k))
						break;
				}
		}

		// Dirección de un bit de patrón: 1 alcista (piso), -1 bajista (techo)
		private static int Direccion(int b)
		{
			return (b & (EbAlc | Toro180 | Bt | Gbr | Rbi | NrbPiso | NbPiso | BmgToro)) != 0 ? 1 : -1;
		}

		// Filtro de confirmación de cada patrón (el de mayor mejora del % a 1R en el estudio), evaluado en la vela que se cierra. Las
		// señales de reversa (180, BT / TT, GBR / RBR, NRB, NB) también se muestran si tocan el VWAP o una banda (ReversaEnBandaVwap).
		private bool Confirma(int b, int dir)
		{
			bool reversa = (b & (Toro180 | Oso180 | Bt | Tt | Gbr | Rbr | NrbPiso | NrbTecho | NbPiso | NbTecho)) != 0;
			if (reversa && ReversaEnBandaVwap && TocaVwap(dir))
				return true;
			if (b == EbAlc || b == EbBaj)                  // subasta fallida en el área de valor de ayer y ATR alto
				return (Barrido(dir) & 32) != 0 && AtrAlto();
			if (b == Toro180 || b == Oso180)               // volumen menor que el de las 2 velas anteriores y ATR alto
				return VolumenBajo() && AtrAlto();
			if (b == Bt || b == Tt)                        // tras un movimiento fuerte en contra y barrido de liquidez
				return Eficiencia(dir) <= -0.3 && Barrido(dir) != 0;
			if (b == Gbr || b == Rbr)                      // 20mm de 15 y 60 min a favor y barrido de liquidez
				return PendienteMayor(dir) && Barrido(dir) != 0;
			if (b == NrbPiso || b == NrbTecho)             // cierre en el 30 % a favor y barrido de liquidez
				return Clv(dir) >= 0.7 && Barrido(dir) != 0;
			if (b == NbPiso || b == NbTecho)               // cierre en el 30 % a favor y kill zone de NY 09:30-11:30
			{
				int hh = Hhmm(ToNy(Time[0]));
				return Clv(dir) >= 0.7 && hh > 930 && hh <= 1130;
			}
			return TresTemporalidades(dir);                // RBI / GBI y BMG
		}

		private static int Hhmm(DateTime et) { return et.Hour * 100 + et.Minute; }

		// La mecha toca el VWAP o una de sus bandas visibles y la vela cierra del lado de la reversa (compra: mínimo <= línea < cierre)
		private bool TocaVwap(int dir)
		{
			if (!VerVwap)
				return false;
			for (int p = 3; p <= 9; p++)
			{
				if (!LineaVisible(p) || !Values[p].IsValidDataPoint(0))
					continue;
				double l = Values[p][0];
				if (dir == 1 ? Low[0] <= l && Close[0] > l : High[0] >= l && Close[0] < l)
					return true;
			}
			return false;
		}

		// Posición del cierre en el rango de la vela, medida desde el extremo contrario a la señal (1 = cierra en su extremo a favor)
		private double Clv(int dir)
		{
			double r = High[0] - Low[0];
			return r > 0 ? (dir == 1 ? Close[0] - Low[0] : High[0] - Close[0]) / r : 0.5;
		}

		private bool VolumenBajo() { return Volume[0] < Volume[1] && Volume[0] < Volume[2]; }

		private bool AtrAlto() { return atrLargo[0] > 0 && atr[0] >= PremiumAtrMin * atrLargo[0]; }

		// Eficiencia de las 20 últimas velas en la dirección de la señal (Kaufman): -1 = caída limpia contra una señal de compra
		private double Eficiencia(int dir)
		{
			double s = 0;
			for (int k = 0; k < 20; k++)
				s += Math.Abs(Close[k] - Close[k + 1]);
			return s > 0 ? dir * (Close[0] - Close[20]) / s : 0;
		}

		// Velas escalonadas antes de esta (máximos y mínimos más bajos para una señal de compra)
		private int Escalones(int dir)
		{
			int e = 0;
			for (int j = 1; j <= 30 && j + 1 <= CurrentBar; j++)
			{
				if (dir == 1 ? High[j] < High[j + 1] && Low[j] < Low[j + 1] : High[j] > High[j + 1] && Low[j] > Low[j + 1]) e++;
				else break;
			}
			return e;
		}

		// 3 temporalidades: en la última vela cerrada de 15 y de 60 min el cierre está del lado de la señal respecto a su 20mm y la
		// 20mm apunta en esa dirección
		private bool TresTemporalidades(int dir)
		{
			if (CurrentBars[1] < 21 || CurrentBars[2] < 21)
				return false;
			return (Closes[1][0] - sma15[0]) * dir > 0 && (sma15[0] - sma15[1]) * dir > 0
				&& (Closes[2][0] - sma60[0]) * dir > 0 && (sma60[0] - sma60[1]) * dir > 0;
		}

		private bool PendienteMayor(int dir)
		{
			if (CurrentBars[1] < 21 || CurrentBars[2] < 21)
				return false;
			return (sma15[0] - sma15[1]) * dir > 0 && (sma60[0] - sma60[1]) * dir > 0;
		}

		// Barrido de liquidez: la vela o la anterior pasa un nivel (mínimo para una señal de compra) y la vela cierra de vuelta.
		// Bits: 1 RTH de ayer, 2 overnight (en RTH), 4 Asia (02:00-16:00), 8 Londres (05:00-16:00), 16 Initial Balance (10:30-16:00),
		// 32 área de valor de ayer, 64 último pivote de 60 velas
		private int Barrido(int dir)
		{
			int hh = Hhmm(ToNy(Time[0]));
			bool rth = hh > 930 && hh <= 1600;
			double ext = dir == 1 ? Math.Min(Low[0], Low[1]) : Math.Max(High[0], High[1]);
			double[] niv = {
				sesAntValida ? (dir == 1 ? sesAntL : sesAntH) : double.NaN,
				rth ? (dir == 1 ? onLf : onHf) : double.NaN,
				hh > 200 && hh <= 1600 ? (dir == 1 ? asLf : asHf) : double.NaN,
				hh > 500 && hh <= 1600 ? (dir == 1 ? lnLf : lnHf) : double.NaN,
				hh > 1030 && hh <= 1600 ? (dir == 1 ? ibL : ibH) : double.NaN,
				dir == 1 ? val : vah,
				PivoteReciente(dir) };
			int m = 0;
			for (int i = 0; i < niv.Length; i++)
				if (!double.IsNaN(niv[i]) && (dir == 1 ? ext < niv[i] && Close[0] > niv[i] : ext > niv[i] && Close[0] < niv[i]))
					m |= 1 << i;
			return m;
		}

		// Último pivote del lado del barrido (mínimo para una compra) de las últimas 60 velas
		private double PivoteReciente(int dir)
		{
			for (int i = pivotes.Count - 1; i >= 0; i--)
			{
				OvPivote pv = pivotes[i];
				if (CurrentBar - pv.Idx > 60)
					break;
				if (pv.Alto == (dir == -1))
					return pv.Precio;
			}
			return double.NaN;
		}

		// Niveles de liquidez de la sesión (hora de NY): overnight 18:00-09:30, Asia 18:00-02:00, Londres 02:00-05:00, cada uno fijo
		// al terminar; Initial Balance 09:30-10:30. La sesión empieza con la primera vela que cierra después de las 18:00.
		private void ActualizarSesiones()
		{
			DateTime et = ToNy(Time[0]);
			int hh = Hhmm(et), hp = Hhmm(etSes);
			bool nuevo = CurrentBar == 0 || (hh > 1800 && (hp <= 1800 || etSes.Date != et.Date)) || (et - etSes).TotalHours > 6;
			if (nuevo)
			{
				onH = asH = High[0]; onL = asL = Low[0];
				lnH = lnL = onHf = onLf = asHf = asLf = lnHf = lnLf = ibH = ibL = double.NaN;
			}
			if (!nuevo && (hh > 1800 || hh <= 930) && double.IsNaN(onHf)) { onH = Math.Max(onH, High[0]); onL = Math.Min(onL, Low[0]); }
			if (hh > 930 && hh <= 1800 && double.IsNaN(onHf)) { onHf = onH; onLf = onL; }
			if (!nuevo && (hh > 1800 || hh <= 200) && double.IsNaN(asHf)) { asH = Math.Max(asH, High[0]); asL = Math.Min(asL, Low[0]); }
			if (hh > 200 && hh <= 1800 && double.IsNaN(asHf)) { asHf = asH; asLf = asL; }
			if (hh > 200 && hh <= 500)
			{
				lnH = double.IsNaN(lnH) ? High[0] : Math.Max(lnH, High[0]);
				lnL = double.IsNaN(lnL) ? Low[0] : Math.Min(lnL, Low[0]);
			}
			if (hh > 500 && hh <= 1800 && double.IsNaN(lnHf) && !double.IsNaN(lnH)) { lnHf = lnH; lnLf = lnL; }
			bool rth = hh > 930 && hh <= 1600 && et.DayOfWeek != DayOfWeek.Saturday && et.DayOfWeek != DayOfWeek.Sunday;
			bool primera = rth && (hp <= 930 || hp > 1600 || etSes.Date != et.Date);
			if (primera) { ibH = High[0]; ibL = Low[0]; }
			else if (rth && hh <= 1030 && !double.IsNaN(ibH)) { ibH = Math.Max(ibH, High[0]); ibL = Math.Min(ibL, Low[0]); }
			etSes = et;
		}

		// Pivotes (máximo / mínimo con N velas a cada lado, confirmado N velas después), su ruptura al cierre y el cierre del
		// día anterior (último cierre a las 16:00 NY o antes, fijado al pasar las 16:00)
		private void ActualizarUbicaciones()
		{
			int p = PivoteVelas;
			if (CurrentBar >= 2 * p)
			{
				bool alto = true, bajo = true;
				for (int i = 0; i <= 2 * p; i++)
				{
					if (i == p) continue;
					if (i < p ? High[i] >= High[p] : High[i] > High[p]) alto = false;
					if (i < p ? Low[i] <= Low[p] : Low[i] < Low[p]) bajo = false;
				}
				if (alto) pivotes.Add(new OvPivote { Idx = CurrentBar - p, Precio = High[p], Alto = true });
				if (bajo) pivotes.Add(new OvPivote { Idx = CurrentBar - p, Precio = Low[p], Alto = false });
				if (pivotes.Count > 400)
					pivotes.RemoveAt(0);
			}
			foreach (OvPivote pv in pivotes)
				if (pv.Fin < 0 && (pv.Alto ? Close[0] > pv.Precio : Close[0] < pv.Precio))
					pv.Fin = CurrentBar;

			DateTime et = ToNy(Time[0]);
			int hhmm = et.Hour * 100 + et.Minute, hhPrev = etPrev.Hour * 100 + etPrev.Minute;
			bool habil = et.DayOfWeek != DayOfWeek.Saturday && et.DayOfWeek != DayOfWeek.Sunday;

			// Sesión RTH (velas que cierran entre 09:30 y 16:00 NY) como una vela: la primera vela de cada sesión abre el gap
			// del cap. 18 frente a la sesión anterior
			bmgBar = 0;
			bool rth = habil && hhmm > 930 && hhmm <= 1600;
			bool primera = rth && (CurrentBar == 0 || hhPrev <= 930 || hhPrev > 1600 || etPrev.Date != et.Date);
			if (primera)
			{
				// Gap Toro (Oso) Monstruo: la sesión anterior fue roja (verde) sólida y esta abre sobre su máximo (bajo su mínimo)
				double r = sesAntH - sesAntL;
				bool solida = sesAntValida && r > 0 && Math.Abs(sesAntC - sesAntO) >= MonstruoCuerpoMin * r;
				if (solida && sesAntC < sesAntO && Open[0] > sesAntH) bmgBar = 1;
				else if (solida && sesAntC > sesAntO && Open[0] < sesAntL) bmgBar = -1;
				sesO = Open[0]; sesH = High[0]; sesL = Low[0];
				sesActiva = true;
				perfil.Clear();
			}
			else if (rth && sesActiva)
			{
				sesH = Math.Max(sesH, High[0]);
				sesL = Math.Min(sesL, Low[0]);
			}
			// Perfil de volumen de la sesión RTH: el volumen de cada vela repartido por igual entre sus niveles de PerfilTicks
			if (rth && sesActiva)
			{
				double paso = PerfilTicks * TickSize;
				int a = (int)Math.Floor(Low[0] / paso), b = (int)Math.Floor(High[0] / paso);
				double v = Volume[0] / (b - a + 1);
				for (int k = a; k <= b; k++)
				{
					double x;
					perfil.TryGetValue(k, out x);
					perfil[k] = x + v;
				}
			}

			if (CurrentBar > 0 && hhmm >= 1600 && (hhPrev < 1600 || etPrev.Date != et.Date) && habil)
			{
				pdc = hhmm == 1600 ? Close[0] : Close[1];
				if (sesActiva)
					AreaValor();
				if (sesActiva)
				{
					sesAntO = sesO; sesAntH = sesH; sesAntL = sesL; sesAntC = pdc;
					sesAntValida = true;
					sesActiva = false;
				}
			}
			etPrev = et;
			// Guardado por vela para dibujarlo en OnRender: un plot estiraría la escala del gráfico hasta el PDC
			pdcSerie[0] = pdc;
			vahSerie[0] = vah;
			valSerie[0] = val;
		}

		// Área de valor (70 % del volumen) de la sesión RTH que termina: desde el nivel de más volumen (POC) se añade cada vez el
		// nivel vecino de más volumen, arriba o abajo
		private void AreaValor()
		{
			if (perfil.Count == 0)
				return;
			List<int> ks = new List<int>(perfil.Keys);
			ks.Sort();
			double tot = 0, mx = -1;
			int ip = 0;
			for (int i = 0; i < ks.Count; i++)
			{
				double v = perfil[ks[i]];
				tot += v;
				if (v > mx) { mx = v; ip = i; }
			}
			int lo = ip, hi = ip;
			double acc = perfil[ks[ip]];
			while (acc < 0.7 * tot && (lo > 0 || hi < ks.Count - 1))
			{
				double up = hi < ks.Count - 1 ? perfil[ks[hi + 1]] : -1, dn = lo > 0 ? perfil[ks[lo - 1]] : -1;
				if (up >= dn) { hi++; acc += up; } else { lo--; acc += dn; }
			}
			double paso = PerfilTicks * TickSize;
			vah = (ks[hi] + 1) * paso;
			val = ks[lo] * paso;
		}

		// dir = 1: señal de piso (soporte); dir = -1: señal de techo (resistencia)
		// Setup 1 de Velez (cap. 10.1, págs. 130-145), dir = 1 VBS / -1 VSS, evaluado en la vela que se acaba de cerrar:
		//   - Retroceso: las velas 1..n (n >= VelasSetup) hacen máximos y mínimos más bajos (VBS) cada una, o son rojas seguidas.
		//   - Tendencia: la 20mm (Media 2) sube en la última vela del retroceso y ninguna vela del retroceso cierra bajo ella.
		//   - Entrada: esta vela opera sobre el máximo de la vela anterior (VBS) o bajo su mínimo (VSS).
		//   - Un setup por retroceso: el retroceso debe empezar después de la última entrada en esa dirección.
		//   - Calidad (★): señal de piso / techo en el retroceso, ubicación, hora de reversa o surge de volumen.
		private bool Setup1(int dir, int f)
		{
			int sp = Pendiente20mm, min = VelasSetup;
			if (!(dir == 1 ? High[0] > High[1] : Low[0] < Low[1]))
				return false;
			// Retroceso: escalonado o del color contrario, contado hacia atrás desde la vela 1
			int esc = 0, col = 0;
			for (int j = 1; j < CurrentBar - 1 && j <= 50; j++)
			{
				if (dir == 1 ? High[j] < High[j + 1] && Low[j] < Low[j + 1] : High[j] > High[j + 1] && Low[j] > Low[j + 1]) esc++;
				else break;
			}
			for (int j = 1; j < CurrentBar && j <= 50; j++)
			{
				if ((Close[j] - Open[j]) * dir < 0) col++;
				else break;
			}
			int n = Math.Max(esc >= min ? esc : 0, col >= min ? col : 0);
			if (n == 0)
				return false;
			// Un setup por retroceso
			int inicio = CurrentBar - n;
			if (inicio <= (dir == 1 ? ultimaVbs : ultimaVss))
				return false;
			// Tendencia: 20mm a favor y el retroceso no cierra del otro lado
			if (!Values[1].IsValidDataPoint(1) || !Values[1].IsValidDataPoint(1 + sp) || (Values[1][1] - Values[1][1 + sp]) * dir <= 0)
				return false;
			for (int j = 1; j <= n; j++)
				if (!Values[1].IsValidDataPoint(j) || (Close[j] - Values[1][j]) * dir < 0)
					return false;

			// Calidad
			int senales = dir == 1 ? Bt | Gbr | NrbPiso | NbPiso | Toro180 : Tt | Rbr | NrbTecho | NbTecho | Oso180;
			bool calidad = (f & senales) != 0;
			for (int j = 1; j <= n && !calidad; j++)
				if ((patrones[j] & senales) != 0) calidad = true;
			if (!calidad && CalidadUbicacion && EnUbicacion(dir, n + 1)) calidad = true;
			if (!calidad && CalidadHora && HoraReversa()) calidad = true;
			if (!calidad && CalidadVolumen && CurrentBar > 21 && volSma[1] > 0 && Volume[0] >= VolumenSurge * volSma[1]) calidad = true;

			setups[0] = dir * (calidad ? 2 : 1);
			if (dir == 1) ultimaVbs = CurrentBar; else ultimaVss = CurrentBar;
			return true;
		}

		// Horas de reversa del manual (NY): 10:00, 10:30, 11:15, 12:00, 13:30, 14:15-14:30, 15:00, 15:30, con tolerancia
		private bool HoraReversa()
		{
			DateTime et = ToNy(Time[0]);
			int m = et.Hour * 60 + et.Minute;
			int[] horas = { 600, 630, 675, 720, 810, 900, 930 };
			foreach (int h in horas)
				if (Math.Abs(m - h) <= HoraTolerancia) return true;
			return m >= 855 - HoraTolerancia && m <= 870 + HoraTolerancia;   // 14:15-14:30
		}

		// Compra (venta) de Regalo (cap. 10.2, págs. 146-154): tras una Elephant alcista (bajista) su rango es la zona; una vela
		// verde (roja) que borra el 100 % del cuerpo de la vela roja (verde) anterior, con esa vela dentro de la zona, es la entrada.
		// La zona se cierra con la entrada, con un cierre fuera de su rango o con una nueva Elephant en la misma dirección.
		// Devuelve true si esta vela es una entrada REG confirmada (3 velas de caída / rally y volumen menor que las 2 anteriores).
		private bool Regalo(int f)
		{
			bool confirmada = false;
			for (int i = zonas.Count - 1; i >= 0; i--)
			{
				OvZona z = zonas[i];
				if (z.Fin >= 0 || z.Idx == CurrentBar)
					continue;
				int d = z.Dir;
				// Invalidación: cierre fuera del rango en contra de la Elephant
				if (d == 1 ? Close[0] < z.Bajo : Close[0] > z.Alto)
				{
					z.Fin = CurrentBar;
					continue;
				}
				// Entrada: vela anterior contraria dentro de la zona y esta vela borra todo su cuerpo
				bool previaEnZona = CurrentBar - 1 > z.Idx && (d == 1 ? Low[1] >= z.Bajo && Low[1] <= z.Alto : High[1] <= z.Alto && High[1] >= z.Bajo);
				bool contraria = (Close[1] - Open[1]) * d < 0;
				bool borra = (Close[0] - Open[0]) * d > 0 && (d == 1 ? Close[0] >= Open[1] : Close[0] <= Open[1]);
				if (previaEnZona && contraria && borra)
				{
					z.Fin = CurrentBar;
					if (setups[0] == 0)
					{
						bool calidad = (CalidadUbicacion && EnUbicacion(d, 2)) || (CalidadHora && HoraReversa())
							|| (CalidadVolumen && CurrentBar > 21 && volSma[1] > 0 && Volume[0] >= VolumenSurge * volSma[1]);
						setups[0] = d * (calidad ? 4 : 3);
						z.Confirmada = confirmada = Escalones(d) >= 2 && VolumenBajo();
					}
				}
			}
			// Nueva zona con cada Elephant (reemplaza la zona activa de la misma dirección)
			int dirEb = (f & EbAlc) != 0 ? 1 : (f & EbBaj) != 0 ? -1 : 0;
			if (dirEb != 0)
			{
				foreach (OvZona z in zonas)
					if (z.Fin < 0 && z.Dir == dirEb) z.Fin = CurrentBar;
				zonas.Add(new OvZona { Idx = CurrentBar, Dir = dirEb, Alto = High[0], Bajo = Low[0] });
				if (zonas.Count > 300)
					zonas.RemoveAt(0);
			}
			return confirmada;
		}

		// VWAP con bandas σ ponderadas por volumen (simula el Order Flow VWAP con las velas del gráfico): ancla en hora de Nueva
		// York detectada en la vela que la cruza (las horas de vela son de cierre), nunca en la primera vela cargada; con ancla RTH
		// o personalizada se congela y se oculta al terminar la ventana; bandas ocultas los primeros minutos tras el ancla.
		// Plots: 3 VWAP, 4 / 5 banda 1, 6 / 7 banda 2, 8 / 9 banda 3.
		private void ActualizarVwap()
		{
			if (!VerVwap)
				return;
			DateTime et = VwapTiempo.AEt(Time[0]);
			DateTime inicio;
			bool reset = VwapTiempo.EsInicioAncla(VwapAnclaSel, et, vwapPrevEt, VwapHoraAncla, VwapHoraRollEth, out inicio);
			vwapPrevEt = et;
			if (reset)
			{
				vwapEng.Reiniciar(inicio);
				vwapCongelado = false;
				vwapBandasVistas = false;
			}
			if (vwapEng.NumeroAncla == 0)
				return;
			bool conVentana = (VwapAnclaSel == VwapAncla.RTH || VwapAnclaSel == VwapAncla.Personalizada) && VwapFinVentana > 0;
			if (conVentana)
			{
				int dur = VwapTiempo.AMin(VwapFinVentana) - VwapTiempo.AMin(VwapHoraAncla);
				if (dur <= 0) dur += 1440;
				if ((et - vwapEng.InicioAncla).TotalMinutes > dur)
					vwapCongelado = true;
			}
			if (vwapCongelado)
				return;
			vwapEng.Actualizar(VwapTiempo.Precio(VwapFuente, Open[0], High[0], Low[0], Close[0]), Volume[0], true);
			if (!vwapEng.Valido)
				return;

			Values[3][0] = vwapEng.Vwap;
			if (reset || !LineaVisible(3))
				PlotBrushes[3][0] = Brushes.Transparent;   // sin tramo entre un periodo y el siguiente
			if ((et - vwapEng.InicioAncla).TotalMinutes < VwapOcultarMin)
				return;
			double[] mult = { VwapMult1, VwapMult2, VwapMult3 };
			for (int b = 0; b < 3; b++)
			{
				Values[4 + 2 * b][0] = vwapEng.Banda(mult[b]);
				Values[5 + 2 * b][0] = vwapEng.Banda(-mult[b]);
				if (!vwapBandasVistas || !LineaVisible(4 + 2 * b))
				{
					PlotBrushes[4 + 2 * b][0] = Brushes.Transparent;
					PlotBrushes[5 + 2 * b][0] = Brushes.Transparent;
				}
			}
			vwapBandasVistas = true;
		}

		// velas: cuántas velas forman la señal (el 180 usa 2); su extremo (mínimo en piso, máximo en techo) es el que llega al nivel
		private bool EnUbicacion(int dir, int velas)
		{
			ubicExt = dir == 1 ? Low[0] : High[0];
			for (int j = 1; j < velas && j <= CurrentBar; j++)
				ubicExt = dir == 1 ? Math.Min(ubicExt, Low[j]) : Math.Max(ubicExt, High[j]);
			double d = DistUbicAtr * atr[0];

			// 20mm y 8mm con pendiente a favor
			if (UbicMedias)
				for (int m = 1; m >= 0; m--)
				{
					int sp = Pendiente20mm;
					if (Values[m].IsValidDataPoint(0) && Values[m].IsValidDataPoint(sp) && (Values[m][0] - Values[m][sp]) * dir > 0 && Cerca(Values[m][0], dir, d))
						return true;
				}
			// 200mm plana
			if (Ubic200 && Values[2].IsValidDataPoint(0) && Values[2].IsValidDataPoint(Plana200Velas)
				&& Math.Abs(Values[2][0] - Values[2][Plana200Velas]) <= Plana200Atr * atr[0] && Cerca(Values[2][0], dir, d))
				return true;
			// Cierre del día anterior
			if (UbicPdc && !double.IsNaN(pdc) && Cerca(pdc, dir, d))
				return true;
			// VWAP y sus bandas
			if (UbicVwap && VerVwap)
				for (int p = 3; p <= 9; p++)
					if (LineaVisible(p) && Values[p].IsValidDataPoint(0) && Cerca(Values[p][0], dir, d))
						return true;
			// Extensión: muy lejos de la 20mm
			if (UbicExtension && Values[1].IsValidDataPoint(0) && (dir == 1 ? Values[1][0] - ubicExt : ubicExt - Values[1][0]) >= ExtensionAtr * atr[0])
				return true;
			// Máximos y mínimos anteriores (los 10 más recientes de cada tipo, rotos o no: un máximo roto es soporte)
			if (UbicPivotes)
			{
				int altos = 0, bajos = 0;
				for (int i = pivotes.Count - 1; i >= 0 && (altos < 10 || bajos < 10); i--)
				{
					OvPivote pv = pivotes[i];
					if (pv.Alto ? altos++ >= 10 : bajos++ >= 10) continue;
					if (Cerca(pv.Precio, dir, d))
						return true;
				}
			}
			// Retroceso del 33-55 % del último tramo: piso desde el último mínimo pivote hasta el máximo posterior (techo: espejo)
			if (UbicRetroceso)
				for (int i = pivotes.Count - 1; i >= 0; i--)
				{
					OvPivote pv = pivotes[i];
					if (pv.Alto == (dir == 1))
						continue;
					int desde = Math.Min(CurrentBar - pv.Idx, 300);
					double ext = dir == 1 ? double.MinValue : double.MaxValue;
					for (int j = 1; j < desde; j++)
						ext = dir == 1 ? Math.Max(ext, High[j]) : Math.Min(ext, Low[j]);
					double tramo = (ext - pv.Precio) * dir;
					if (desde >= 2 && tramo > 0)
					{
						double a = ext - dir * RetrocesoMin * tramo, b = ext - dir * RetrocesoMax * tramo;
						double lvl = ubicExt;
						if (lvl <= Math.Max(a, b) + d && lvl >= Math.Min(a, b) - d)
							return true;
					}
					break;
				}
			return false;
		}

		// La vela llega al nivel (o lo cruza) y el cierre no queda más allá del área
		private bool Cerca(double lvl, int dir, double d)
		{
			return dir == 1 ? ubicExt <= lvl + d && Close[0] >= lvl - d : ubicExt >= lvl - d && Close[0] <= lvl + d;
		}

		private DateTime ToNy(DateTime t)
		{
			if (nyTz == null)
				return t;
			try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo, nyTz); }
			catch (ArgumentException) { return t; }
		}

		// k = velas atrás de la vela ignorada; la vela k + 1 da la dirección
		private bool MarcarIgnorada(int k)
		{
			for (int dir = 1; dir >= -1; dir -= 2)
			{
				int v1 = k + 1;
				double r1 = High[v1] - Low[v1];
				// Vela 1 sólida en la dirección y vela k (la ignorada) del color contrario
				bool v1Ok = r1 > 0 && (Close[v1] - Open[v1]) * dir > 0 && Math.Abs(Close[v1] - Open[v1]) >= IgnCuerpoV1 * r1;
				bool vkOk = (Close[k] - Open[k]) * dir < 0;
				// Esta vela opera más allá del extremo de la ignorada (RBI: su máximo; GBI: su mínimo); ninguna vela entre ambas lo
				// hizo antes, y ninguna pierde el otro extremo de la ignorada (Tip 2, pág. 292)
				bool rompe = dir == 1 ? (IgnPorCierre ? Close[0] > High[k] : High[0] > High[k]) : (IgnPorCierre ? Close[0] < Low[k] : Low[0] < Low[k]);
				if (!v1Ok || !vkOk || !rompe)
					continue;
				bool ok = true;
				for (int j = 0; j < k; j++)
				{
					if (dir == 1 ? Low[j] < Low[k] : High[j] > High[k])
						ok = false;
					if (j > 0 && (dir == 1 ? (IgnPorCierre ? Close[j] > High[k] : High[j] > High[k]) : (IgnPorCierre ? Close[j] < Low[k] : Low[j] < Low[k])))
						ok = false;
				}
				// La ignorada en el tercio extremo de la vela 1 sin superarla (RBI: tercio superior, sin pasar su máximo)
				if (ok && IgnTercio)
					ok = dir == 1 ? Low[k] >= High[v1] - IgnZona * r1 && High[k] <= High[v1] : High[k] <= Low[v1] + IgnZona * r1 && Low[k] >= Low[v1];
				// Tendencia: la 20mm (Media 2) va a favor y la ignorada queda de su lado (RBI: su mínimo sobre la media)
				if (ok && IgnTendencia)
				{
					int sp = Pendiente20mm;
					ok = Values[1].IsValidDataPoint(k) && Values[1].IsValidDataPoint(k + sp) && (Values[1][k] - Values[1][k + sp]) * dir > 0
						&& (dir == 1 ? Low[k] > Values[1][k] : High[k] < Values[1][k]);
				}
				if (ok)
				{
					patrones[k] = patrones[k] | (dir == 1 ? Rbi : Gbi);
					// Confirmación: 3 temporalidades en la vela que dispara (demasiado raros para medir otro filtro)
					if (TresTemporalidades(dir))
						confirmados[k] = confirmados[k] | (dir == 1 ? Rbi : Gbi);
					Pintar(k);
					return true;
				}
			}
			return false;
		}

		// Color de la vela: el del patrón confirmado más fuerte (las velas sin patrón confirmado quedan con su color normal)
		private void Pintar(int barsAgo)
		{
			if (!PintarVelas)
				return;
			Brush b = ColorPrincipal(confirmados[barsAgo]);
			if (b == null)
				return;
			BarBrushes[barsAgo]           = b;
			CandleOutlineBrushes[barsAgo] = b;
		}

		private Brush ColorPrincipal(int f)
		{
			if ((f & BmgToro) != 0) return ColorBmgToro;
			if ((f & BmgOso) != 0) return ColorBmgOso;
			if ((f & Toro180) != 0) return ColorToro180;
			if ((f & Oso180) != 0) return ColorOso180;
			if ((f & EbAlc) != 0) return ColorEbAlc;
			if ((f & EbBaj) != 0) return ColorEbBaj;
			if ((f & Bt) != 0) return ColorBt;
			if ((f & Tt) != 0) return ColorTt;
			if ((f & Gbr) != 0) return ColorGbr;
			if ((f & Rbr) != 0) return ColorRbr;
			if ((f & Rbi) != 0) return ColorRbi;
			if ((f & Gbi) != 0) return ColorGbi;
			if ((f & NrbPiso) != 0) return ColorNrbPiso;
			if ((f & NrbTecho) != 0) return ColorNrbTecho;
			if ((f & NbPiso) != 0) return ColorNbPiso;
			if ((f & NbTecho) != 0) return ColorNbTecho;
			return null;
		}

		// Siglas de los patrones confirmados en orden de prioridad: alcistas (debajo de la vela) y bajistas (encima)
		private void Siglas(int f, List<KeyValuePair<string, Brush>> abajo, List<KeyValuePair<string, Brush>> arriba)
		{
			if ((f & BmgToro) != 0) abajo.Add(Par("+BMG", ColorBmgToro));
			if ((f & Toro180) != 0) abajo.Add(Par("+180", ColorToro180));
			if ((f & EbAlc) != 0) abajo.Add(Par("EB", ColorEbAlc));
			if ((f & Bt) != 0) abajo.Add(Par("BT", ColorBt));
			if ((f & Gbr) != 0) abajo.Add(Par("GBR", ColorGbr));
			if ((f & Rbi) != 0) abajo.Add(Par("RBI", ColorRbi));
			if ((f & NrbPiso) != 0) abajo.Add(Par("NRB", ColorNrbPiso));
			if ((f & NbPiso) != 0) abajo.Add(Par("NB", ColorNbPiso));
			if ((f & BmgOso) != 0) arriba.Add(Par("-BMG", ColorBmgOso));
			if ((f & Oso180) != 0) arriba.Add(Par("-180", ColorOso180));
			if ((f & EbBaj) != 0) arriba.Add(Par("EB", ColorEbBaj));
			if ((f & Tt) != 0) arriba.Add(Par("TT", ColorTt));
			if ((f & Rbr) != 0) arriba.Add(Par("RBR", ColorRbr));
			if ((f & Gbi) != 0) arriba.Add(Par("GBI", ColorGbi));
			if ((f & NrbTecho) != 0) arriba.Add(Par("NRB", ColorNrbTecho));
			if ((f & NbTecho) != 0) arriba.Add(Par("NB", ColorNbTecho));
		}

		private static KeyValuePair<string, Brush> Par(string s, Brush b) { return new KeyValuePair<string, Brush>(s, b); }

		// Siglas y leyenda dibujadas solo para las velas visibles
		// Plots visibles: 0-2 medias, 3 VWAP, 4/5 banda 1, 6/7 banda 2, 8/9 banda 3 (las ocultas se siguen calculando)
		private bool LineaVisible(int p)
		{
			switch (p)
			{
				case 0: return VerMedia1;
				case 1: return VerMedia2;
				case 2: return VerMedia3;
				case 3: return VerVwapCentral;
				case 4: case 5: return VwapBanda1;
				case 6: case 7: return VwapBanda2;
				default: return VwapBanda3;
			}
		}

		// Escala automática solo con las velas visibles: las bandas lejanas y la 200mm no encogen las velas
		public override void OnCalculateMinMax()
		{
			if (!EscalaSoloVelas || ChartBars == null || Bars == null)
			{
				base.OnCalculateMinMax();
				return;
			}
			double mn = double.MaxValue, mx = double.MinValue;
			for (int idx = Math.Max(ChartBars.FromIndex, 0); idx <= ChartBars.ToIndex && idx < ChartBars.Bars.Count; idx++)
			{
				mn = Math.Min(mn, ChartBars.Bars.GetLow(idx));
				mx = Math.Max(mx, ChartBars.Bars.GetHigh(idx));
			}
			if (mn <= mx)
			{
				MinValue = mn;
				MaxValue = mx;
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (Bars == null || ChartBars == null || IsInHitTest)
				return;

			using (SharpDX.DirectWrite.TextFormat fmt = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, TamanoSigla))
			{
				fmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				float lineH = TamanoSigla + 2f, gap = 4f, w = 40f;
				Dictionary<Brush, SharpDX.Direct2D1.Brush> dx = new Dictionary<Brush, SharpDX.Direct2D1.Brush>();
				try
				{
					using (SharpDX.Direct2D1.StrokeStyle guion = new SharpDX.Direct2D1.StrokeStyle(NinjaTrader.Core.Globals.D2DFactory,
						new SharpDX.Direct2D1.StrokeStyleProperties { DashStyle = SharpDX.Direct2D1.DashStyle.Dash }))
					{
						// Máximos y mínimos anteriores: desde el pivote hasta que una vela cierra más allá; de los vigentes solo los
						// más recientes de cada tipo
						if (VerPivotes)
						{
							int altos = 0, bajos = 0;
							for (int i = pivotes.Count - 1; i >= 0; i--)
							{
								OvPivote pv = pivotes[i];
								if (pv.Fin < 0 && (pv.Alto ? altos++ : bajos++) >= PivotesVigentes)
									continue;
								int fin = pv.Fin < 0 ? CurrentBars[0] : pv.Fin;
								if (fin < ChartBars.FromIndex || pv.Idx > ChartBars.ToIndex)
									continue;
								float y = chartScale.GetYByValue(pv.Precio);
								RenderTarget.DrawLine(new SharpDX.Vector2(chartControl.GetXByBarIndex(ChartBars, pv.Idx), y),
									new SharpDX.Vector2(chartControl.GetXByBarIndex(ChartBars, fin), y), DxBrush(pv.Alto ? ColorPivoteAlto : ColorPivoteBajo, dx), 1f, guion);
							}
						}
						// Cierre y área de valor del día anterior: un tramo por día, sin unir un día con el siguiente
						if (VerPdc)
							Tramos(pdcSerie, ColorPdc, 2f, guion, chartControl, chartScale, dx);
						if (VerAreaValor)
						{
							Tramos(vahSerie, ColorAreaValor, 1f, guion, chartControl, chartScale, dx);
							Tramos(valSerie, ColorAreaValor, 1f, guion, chartControl, chartScale, dx);
						}
					}

					// Zonas de Regalo que terminaron en una entrada REG confirmada: el rango de la Elephant desde su vela hasta la entrada
					if (VerRegalo)
						foreach (OvZona z in zonas)
						{
							if (!z.Confirmada)
								continue;
							int fin = z.Fin < 0 ? CurrentBars[0] : z.Fin;
							if (fin < ChartBars.FromIndex || z.Idx > ChartBars.ToIndex)
								continue;
							float x0 = chartControl.GetXByBarIndex(ChartBars, z.Idx), x1 = chartControl.GetXByBarIndex(ChartBars, fin);
							float yTop = chartScale.GetYByValue(z.Alto), yBot = chartScale.GetYByValue(z.Bajo);
							using (SharpDX.Direct2D1.Brush b = (z.Dir == 1 ? ColorRegaloAlc : ColorRegaloBaj).ToDxBrush(RenderTarget))
							{
								b.Opacity = OpacidadRegalo / 100f;
								RenderTarget.FillRectangle(new SharpDX.RectangleF(x0, yTop, Math.Max(x1 - x0, 1f), yBot - yTop), b);
							}
						}

					List<KeyValuePair<string, Brush>> abajo = new List<KeyValuePair<string, Brush>>(), arriba = new List<KeyValuePair<string, Brush>>();
					for (int idx = Math.Max(ChartBars.FromIndex, 0); idx <= ChartBars.ToIndex && idx <= CurrentBars[0]; idx++)
					{
						int cf = confirmados.GetValueAt(idx);
						int f = VerSiglas ? cf : 0;
						int st = (cf & SetupConf) != 0 ? setups.GetValueAt(idx) : 0;   // solo el setup confirmado
						if (f == 0 && st == 0)
							continue;
						abajo.Clear(); arriba.Clear();
						if (f != 0)
							Siglas(f, abajo, arriba);
						float x = chartControl.GetXByBarIndex(ChartBars, idx);
						float yLow = chartScale.GetYByValue(ChartBars.Bars.GetLow(idx)), yHigh = chartScale.GetYByValue(ChartBars.Bars.GetHigh(idx));
						for (int i = 0; i < abajo.Count; i++)
							Texto(abajo[i], x - w / 2, yLow + gap + i * lineH, w, lineH, fmt, dx);
						for (int i = 0; i < arriba.Count; i++)
							Texto(arriba[i], x - w / 2, yHigh - gap - (i + 1) * lineH, w, lineH, fmt, dx);
						// Setups (VBS / VSS / Regalo): flecha y sigla más allá de las siglas de patrones
						if (st != 0)
						{
							bool alc = st > 0;
							int tipo = Math.Abs(st);
							string sigla = (tipo == 3 || tipo == 4 ? "REG" : alc ? "VBS" : "VSS") + (tipo == 2 || tipo == 4 ? "★" : "");
							Brush c = tipo >= 3 ? (alc ? ColorRegaloAlc : ColorRegaloBaj) : (alc ? ColorVbs : ColorVss);
							if (alc)
							{
								float y0 = yLow + gap + abajo.Count * lineH;
								Texto(Par("▲", c), x - w / 2, y0, w, lineH, fmt, dx);
								Texto(Par(sigla, c), x - w / 2, y0 + lineH, w, lineH, fmt, dx);
							}
							else
							{
								float y0 = yHigh - gap - arriba.Count * lineH;
								Texto(Par("▼", c), x - w / 2, y0 - lineH, w, lineH, fmt, dx);
								Texto(Par(sigla, c), x - w / 2, y0 - 2 * lineH, w, lineH, fmt, dx);
							}
						}
					}
					// Etiquetas del VWAP y sus bandas a la derecha de la última vela visible con valor
					if (VerVwap && VwapEtiquetas)
					{
						int ult = Math.Min(ChartBars.ToIndex, CurrentBars[0]);
						string[] nombres = { "VWAP", "+1", "−1", "+2", "−2", "+3", "−3" };
						// A la derecha de la última vela, sin salir del panel (si no hay margen derecho, pegadas al borde)
						using (SharpDX.DirectWrite.TextFormat fmtV = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, VwapTamanoEtiqueta))
						{
							fmtV.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
							float altoEt = VwapTamanoEtiqueta + 4f, anchoEt = VwapTamanoEtiqueta * 3.5f;
							float xl = Math.Min(chartControl.GetXByBarIndex(ChartBars, ult) + 8, ChartPanel.X + ChartPanel.W - anchoEt);
							for (int p = 3; p <= 9; p++)
							{
								if (!LineaVisible(p) || !Values[p].IsValidDataPointAt(ult))
									continue;
								double v = Values[p].GetValueAt(ult);
								Texto(Par(nombres[p - 3], Plots[p].Brush), xl, chartScale.GetYByValue(v) - altoEt / 2, anchoEt, altoEt, fmtV, dx);
							}
						}
					}
					if (VerLeyenda)
					{
						fmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						KeyValuePair<string, Brush>[] ley =
						{
							Par("Señales confirmadas (EB sin filtro):", chartControl.Properties.ChartText),
							Par("EB Elephant: todas, sin filtro", ColorEbAlc),
							Par("±180: volumen bajo (VSA) + ATR alto | banda VWAP", ColorToro180),
							Par("BT / TT colas: tras caída/rally fuerte + barrido | banda VWAP", ColorBt),
							Par("GBR / RBR reversa: 20mm 15 y 60 min + barrido | banda VWAP", ColorGbr),
							Par("NRB rango estrecho: cierre a favor + barrido | banda VWAP", ColorNrbPiso),
							Par("NB cuerpo estrecho: cierre a favor + NY 09:30-11:30 | banda VWAP", ColorNbPiso),
							Par("RBI / GBI ignorada: 3 temporalidades", ColorRbi),
							Par("±BMG gap monstruo: 3 temporalidades", ColorBmgToro),
							Par("▲VBS ▼VSS Setup 1: 3 temporalidades + cierre a favor", ColorVbs),
							Par("REG regalo: 3 velas de caída/rally + volumen bajo", ColorRegaloAlc),
							Par("| banda VWAP: la mecha toca el VWAP o una banda y cierra dentro", chartControl.Properties.ChartText)
						};
						float anchoLey = 360;
						float x0 = ChartPanel.X + 8, y0 = ChartPanel.Y + ChartPanel.H - 30 - ley.Length * lineH;
						SharpDX.RectangleF caja = new SharpDX.RectangleF(x0 - 4, y0 - 4, anchoLey, ley.Length * lineH + 8);
						using (SharpDX.Direct2D1.Brush fondo = chartControl.Properties.ChartBackground.ToDxBrush(RenderTarget))
						using (SharpDX.Direct2D1.Brush borde = chartControl.Properties.ChartText.ToDxBrush(RenderTarget))
						{
							fondo.Opacity = 0.9f;
							borde.Opacity = 0.3f;
							RenderTarget.FillRectangle(caja, fondo);
							RenderTarget.DrawRectangle(caja, borde, 1f);
						}
						for (int i = 0; i < ley.Length; i++)
							Texto(ley[i], x0, y0 + i * lineH, anchoLey, lineH, fmt, dx);
					}
				}
				finally
				{
					foreach (SharpDX.Direct2D1.Brush b in dx.Values)
						b.Dispose();
				}
			}
		}

		// Línea horizontal por cada tramo de velas visibles con el mismo valor de la serie (PDC, VAH, VAL)
		private void Tramos(Series<double> s, Brush c, float ancho, SharpDX.Direct2D1.StrokeStyle estilo, ChartControl cc, ChartScale cs, Dictionary<Brush, SharpDX.Direct2D1.Brush> dx)
		{
			int ini = -1;
			for (int idx = Math.Max(ChartBars.FromIndex, 0); idx <= ChartBars.ToIndex + 1; idx++)
			{
				double v = idx <= Math.Min(ChartBars.ToIndex, CurrentBars[0]) ? s.GetValueAt(idx) : double.NaN;
				double vIni = ini >= 0 ? s.GetValueAt(ini) : double.NaN;
				if (ini >= 0 && (double.IsNaN(v) || v != vIni))
				{
					float y = cs.GetYByValue(vIni);
					RenderTarget.DrawLine(new SharpDX.Vector2(cc.GetXByBarIndex(ChartBars, ini), y),
						new SharpDX.Vector2(cc.GetXByBarIndex(ChartBars, idx - 1), y), DxBrush(c, dx), ancho, estilo);
					ini = -1;
				}
				if (ini < 0 && !double.IsNaN(v))
					ini = idx;
			}
		}

		private SharpDX.Direct2D1.Brush DxBrush(Brush c, Dictionary<Brush, SharpDX.Direct2D1.Brush> dx)
		{
			SharpDX.Direct2D1.Brush b;
			if (!dx.TryGetValue(c, out b))
			{
				b = c.ToDxBrush(RenderTarget);
				dx[c] = b;
			}
			return b;
		}

		private void Texto(KeyValuePair<string, Brush> t, float x, float y, float w, float h, SharpDX.DirectWrite.TextFormat fmt, Dictionary<Brush, SharpDX.Direct2D1.Brush> dx)
		{
			SharpDX.Direct2D1.Brush b;
			if (!dx.TryGetValue(t.Value, out b))
			{
				b = t.Value.ToDxBrush(RenderTarget);
				dx[t.Value] = b;
			}
			RenderTarget.DrawText(t.Key, fmt, new SharpDX.RectangleF(x, y, w, h), b);
		}

		private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
		{
			SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b));
			s.Freeze();
			return s;
		}

		#region Properties
		// --- 1. Medias móviles ---
		[Display(Name = "Mostrar media 1", Description = "Oculta solo el dibujo: la media se sigue usando en la ubicación y los setups.", Order = 0, GroupName = "1. Medias móviles")]
		public bool VerMedia1 { get; set; }

		[Display(Name = "Mostrar media 2", Description = "Oculta solo el dibujo: la media 2 (20mm) se sigue usando en la ubicación y los setups.", Order = 3, GroupName = "1. Medias móviles")]
		public bool VerMedia2 { get; set; }

		[Display(Name = "Mostrar media 3", Description = "Oculta solo el dibujo: la media se sigue usando en la ubicación.", Order = 6, GroupName = "1. Medias móviles")]
		public bool VerMedia3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Media 1: tipo", Order = 1, GroupName = "1. Medias móviles")]
		public OvTipoMedia Ma1Tipo { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Media 1: periodo", Order = 2, GroupName = "1. Medias móviles")]
		public int Ma1Periodo { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Media 2: tipo", Order = 4, GroupName = "1. Medias móviles")]
		public OvTipoMedia Ma2Tipo { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Media 2: periodo", Order = 5, GroupName = "1. Medias móviles")]
		public int Ma2Periodo { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Media 3: tipo", Order = 7, GroupName = "1. Medias móviles")]
		public OvTipoMedia Ma3Tipo { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Media 3: periodo", Order = 8, GroupName = "1. Medias móviles")]
		public int Ma3Periodo { get; set; }

		// --- 2. Patrones y visual ---
		[Display(Name = "Elephant (EB)", Order = 1, GroupName = "2. Patrones")]
		public bool VerElephant { get; set; }

		[Display(Name = "Toro / Oso 180", Order = 2, GroupName = "2. Patrones")]
		public bool VerP180 { get; set; }

		[Display(Name = "Colas de piso / techo (BT / TT)", Order = 3, GroupName = "2. Patrones")]
		public bool VerColas { get; set; }

		[Display(Name = "Verde / roja de reversa (GBR / RBR)", Order = 4, GroupName = "2. Patrones")]
		public bool VerReversa { get; set; }

		[Display(Name = "Roja / verde ignorada (RBI / GBI)", Order = 5, GroupName = "2. Patrones")]
		public bool VerIgnorada { get; set; }

		[Display(Name = "Rango estrecho (NRB)", Order = 6, GroupName = "2. Patrones")]
		public bool VerNrb { get; set; }

		[Display(Name = "Cuerpo estrecho (NB)", Order = 7, GroupName = "2. Patrones")]
		public bool VerNb { get; set; }

		[Display(Name = "Gap Toro / Oso Monstruo (BMG)", Description = "Primera vela de la sesión RTH (09:30 NY) que abre sobre el máximo (bajo el mínimo) de una sesión anterior roja (verde) sólida.", Order = 12, GroupName = "2. Patrones")]
		public bool VerMonstruo { get; set; }

		[Range(0.1, 1.0)]
		[Display(Name = "BMG: cuerpo mínimo de la sesión anterior (fracción del rango)", Order = 13, GroupName = "2. Patrones")]
		public double MonstruoCuerpoMin { get; set; }

		// Patrones (bits EbAlc, Toro180, Bt...) y setup (1 / 2 VBS, 3 / 4 REG; negativos en venta) de una vela, para estrategias
		// que confirman con las señales de Velez. RBI / GBI se marcan en la vela ignorada cuando se dispara, velas después.
		public int PatronesVela(int barsAgo)
		{
			Update();
			return patrones[barsAgo];
		}

		public int SetupVela(int barsAgo)
		{
			Update();
			return setups[barsAgo];
		}

		// Patrones confirmados de una vela (mismos bits que PatronesVela, más SetupConf si su setup VBS / VSS / REG está confirmado)
		public int ConfirmadosVela(int barsAgo)
		{
			Update();
			return confirmados[barsAgo];
		}

		// --- 8. Confirmación ---
		[Display(Name = "Reversas en el VWAP y sus bandas", Description = "Además de su filtro, 180, BT / TT, GBR / RBR, NRB y NB se muestran si la mecha toca el VWAP o una banda visible y la vela cierra del lado de la reversa. En el estudio (VWAP semanal, MNQ y MES) acertaron como sin filtro: ~48 % a 1R.", Order = 0, GroupName = "8. Confirmación")]
		public bool ReversaEnBandaVwap { get; set; }

		[Range(0.5, 5.0)]
		[Display(Name = "EB y 180: ATR alto (x su media)", Description = "Confirmación de EB y 180: ATR(14) >= este múltiplo de su media de las velas de abajo (1,2 en el estudio).", Order = 1, GroupName = "8. Confirmación")]
		public double PremiumAtrMin { get; set; }

		[Range(20, 5000)]
		[Display(Name = "Velas de la media del ATR", Order = 2, GroupName = "8. Confirmación")]
		public int PremiumAtrVelas { get; set; }

		[Range(1, 1000)]
		[Display(Name = "Área de valor: ticks por nivel del perfil", Description = "Perfil de volumen de la sesión RTH (09:30-16:00 NY) con el volumen de cada vela repartido entre sus niveles; área de valor = 70 % del volumen alrededor del POC. 4 ticks = 1 punto en MNQ / MES.", Order = 3, GroupName = "8. Confirmación")]
		public int PerfilTicks { get; set; }

		[Display(Name = "Dibujar VAH / VAL del día anterior", Description = "Área de valor de la sesión RTH anterior: la subasta fallida que confirma las EB.", Order = 4, GroupName = "8. Confirmación")]
		public bool VerAreaValor { get; set; }

		[XmlIgnore] [Display(Name = "Color de VAH / VAL", Order = 5, GroupName = "8. Confirmación")] public Brush ColorAreaValor { get; set; }
		[Browsable(false)] public string ColorAreaValorS { get { return Serialize.BrushToString(ColorAreaValor); } set { ColorAreaValor = Serialize.StringToBrush(value); } }

		// --- 7. VWAP Order Flow (simulado) ---
		[Display(Name = "Mostrar VWAP", Description = "VWAP y bandas σ ponderadas por volumen calculados con las velas del gráfico (motor AddOns\\VwapEngine.cs), sin licencia Order Flow+. Colores, grosor y estilo en los plots \"VWAP\" y \"VWAP ±1/±2/±3\".", Order = 1, GroupName = "7. VWAP")]
		public bool VerVwap { get; set; }

		[Display(Name = "Ancla", Description = "RTH (hora de ancla), ETH (rollo 18:00), Semanal, Mensual o Personalizada (hora de ancla), en hora de Nueva York.", Order = 2, GroupName = "7. VWAP")]
		public VwapAncla VwapAnclaSel { get; set; }

		[Range(0, 2359)]
		[Display(Name = "Hora de ancla (NY, HHMM)", Description = "RTH / Personalizada.", Order = 3, GroupName = "7. VWAP")]
		public int VwapHoraAncla { get; set; }

		[Range(0, 2359)]
		[Display(Name = "Fin de ventana (NY, HHMM)", Description = "RTH / Personalizada: el VWAP se congela y se oculta después de esta hora hasta el próximo ancla. 0 = sin ventana.", Order = 4, GroupName = "7. VWAP")]
		public int VwapFinVentana { get; set; }

		[Range(0, 2359)]
		[Display(Name = "Rollo ETH (NY, HHMM)", Order = 5, GroupName = "7. VWAP")]
		public int VwapHoraRollEth { get; set; }

		[Display(Name = "Precio fuente", Order = 6, GroupName = "7. VWAP")]
		public VwapPrecio VwapFuente { get; set; }

		[Display(Name = "Mostrar VWAP (línea central)", Order = 7, GroupName = "7. VWAP")]
		public bool VerVwapCentral { get; set; }

		[Display(Name = "Mostrar banda ±1", Order = 7, GroupName = "7. VWAP")]
		public bool VwapBanda1 { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "Banda 1: multiplicador (σ)", Order = 8, GroupName = "7. VWAP")]
		public double VwapMult1 { get; set; }

		[Display(Name = "Mostrar banda ±2", Order = 9, GroupName = "7. VWAP")]
		public bool VwapBanda2 { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "Banda 2: multiplicador (σ)", Order = 10, GroupName = "7. VWAP")]
		public double VwapMult2 { get; set; }

		[Display(Name = "Mostrar banda ±3", Order = 11, GroupName = "7. VWAP")]
		public bool VwapBanda3 { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "Banda 3: multiplicador (σ)", Order = 12, GroupName = "7. VWAP")]
		public double VwapMult3 { get; set; }

		[Range(0, 240)]
		[Display(Name = "Ocultar bandas los primeros (minutos)", Description = "Tras el ancla σ es casi cero; el VWAP se dibuja desde el ancla.", Order = 13, GroupName = "7. VWAP")]
		public int VwapOcultarMin { get; set; }

		[Display(Name = "Etiquetas de las bandas", Description = "Número de cada banda (VWAP, +1, −1, +2…) a la derecha de la última vela.", Order = 14, GroupName = "7. VWAP")]
		public bool VwapEtiquetas { get; set; }

		[Range(6, 30)]
		[Display(Name = "Tamaño de las etiquetas", Order = 15, GroupName = "7. VWAP")]
		public int VwapTamanoEtiqueta { get; set; }

		[Display(Name = "Ubicación: VWAP y bandas", Description = "El VWAP y sus bandas visibles cuentan como ítem de ubicación (sección 5) para la calidad ★ de los setups.", Order = 16, GroupName = "7. VWAP")]
		public bool UbicVwap { get; set; }

		[Display(Name = "Escala solo con las velas", Description = "La escala automática del gráfico se ajusta a las velas visibles: las bandas lejanas y las medias no encogen las velas (las líneas fuera de la escala se salen del panel).", Order = 17, GroupName = "7. VWAP")]
		public bool EscalaSoloVelas { get; set; }

		// --- 6. Setup 1 (cap. 10) ---
		[Display(Name = "VBS / VSS (Setup 1)", Description = "Retroceso de N velas escalonadas o del color contrario con la 20mm a favor; flecha en la vela que opera más allá del extremo de la vela anterior.", Order = 1, GroupName = "6. Setup 1")]
		public bool VerVbs { get; set; }

		[Range(2, 20)]
		[Display(Name = "Velas del retroceso", Description = "Mínimo de velas con máximos y mínimos más bajos (VBS), o del color contrario seguidas. 2 sirve en tendencias muy fuertes.", Order = 2, GroupName = "6. Setup 1")]
		public int VelasSetup { get; set; }

		[Display(Name = "Compra / venta de Regalo", Description = "Zona del rango de cada Elephant y entrada cuando una vela borra el 100 % de la vela contraria anterior dentro de la zona.", Order = 3, GroupName = "6. Setup 1")]
		public bool VerRegalo { get; set; }

		[Range(0, 100)]
		[Display(Name = "Opacidad de la zona de Regalo (%)", Order = 4, GroupName = "6. Setup 1")]
		public int OpacidadRegalo { get; set; }

		[Display(Name = "Calidad ★: ubicación", Description = "Estrella si el extremo del retroceso está en un ítem de ubicación (sección 5). La señal de piso / techo en el retroceso siempre da estrella.", Order = 5, GroupName = "6. Setup 1")]
		public bool CalidadUbicacion { get; set; }

		[Display(Name = "Calidad ★: hora de reversa", Description = "10:00, 10:30, 11:15, 12:00, 13:30, 14:15-14:30, 15:00, 15:30 NY.", Order = 6, GroupName = "6. Setup 1")]
		public bool CalidadHora { get; set; }

		[Range(0, 30)]
		[Display(Name = "Hora de reversa: tolerancia (minutos)", Order = 7, GroupName = "6. Setup 1")]
		public int HoraTolerancia { get; set; }

		[Display(Name = "Calidad ★: surge de volumen", Order = 8, GroupName = "6. Setup 1")]
		public bool CalidadVolumen { get; set; }

		[Range(1.0, 10.0)]
		[Display(Name = "Surge de volumen (x promedio de 20)", Order = 9, GroupName = "6. Setup 1")]
		public double VolumenSurge { get; set; }

		[XmlIgnore] [Display(Name = "VBS", Order = 17, GroupName = "4. Colores")] public Brush ColorVbs { get; set; }
		[Browsable(false)] public string ColorVbsS { get { return Serialize.BrushToString(ColorVbs); } set { ColorVbs = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "VSS", Order = 18, GroupName = "4. Colores")] public Brush ColorVss { get; set; }
		[Browsable(false)] public string ColorVssS { get { return Serialize.BrushToString(ColorVss); } set { ColorVss = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Regalo de compra (zona y entrada)", Order = 19, GroupName = "4. Colores")] public Brush ColorRegaloAlc { get; set; }
		[Browsable(false)] public string ColorRegaloAlcS { get { return Serialize.BrushToString(ColorRegaloAlc); } set { ColorRegaloAlc = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Regalo de venta (zona y entrada)", Order = 20, GroupName = "4. Colores")] public Brush ColorRegaloBaj { get; set; }
		[Browsable(false)] public string ColorRegaloBajS { get { return Serialize.BrushToString(ColorRegaloBaj); } set { ColorRegaloBaj = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Gap Toro Monstruo (+BMG)", Order = 15, GroupName = "4. Colores")] public Brush ColorBmgToro { get; set; }
		[Browsable(false)] public string ColorBmgToroS { get { return Serialize.BrushToString(ColorBmgToro); } set { ColorBmgToro = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Gap Oso Monstruo (-BMG)", Order = 16, GroupName = "4. Colores")] public Brush ColorBmgOso { get; set; }
		[Browsable(false)] public string ColorBmgOsoS { get { return Serialize.BrushToString(ColorBmgOso); } set { ColorBmgOso = Serialize.StringToBrush(value); } }

		[Display(Name = "Pintar las velas", Order = 8, GroupName = "2. Patrones")]
		public bool PintarVelas { get; set; }

		[Display(Name = "Mostrar siglas", Order = 9, GroupName = "2. Patrones")]
		public bool VerSiglas { get; set; }

		[Display(Name = "Mostrar leyenda", Order = 10, GroupName = "2. Patrones")]
		public bool VerLeyenda { get; set; }

		[Range(6, 24)]
		[Display(Name = "Tamaño de las siglas", Order = 11, GroupName = "2. Patrones")]
		public int TamanoSigla { get; set; }

		// --- 3. Definiciones ---
		[Range(5, 200)]
		[Display(Name = "Velas para promedios", Description = "Velas anteriores para el cuerpo medio y el rango medio.", Order = 1, GroupName = "3. Definiciones")]
		public int Promedio { get; set; }

		[Range(1.0, 10.0)]
		[Display(Name = "EB: cuerpo mínimo (x cuerpo medio)", Order = 2, GroupName = "3. Definiciones")]
		public double EbMultiplicador { get; set; }

		[Range(0.1, 1.0)]
		[Display(Name = "EB: cuerpo mínimo (fracción del rango)", Order = 3, GroupName = "3. Definiciones")]
		public double EbCuerpoMin { get; set; }

		[Range(0.05, 0.5)]
		[Display(Name = "EB: zona de cierre (fracción del rango)", Description = "El cierre queda en esta fracción del extremo de su dirección.", Order = 4, GroupName = "3. Definiciones")]
		public double EbZonaCierre { get; set; }

		[Range(0.1, 10.0)]
		[Display(Name = "180: vela amplia (x rango medio)", Order = 5, GroupName = "3. Definiciones")]
		public double P180AnchoMult { get; set; }

		[Range(0.1, 1.0)]
		[Display(Name = "180: cuerpo mínimo (fracción del rango)", Order = 6, GroupName = "3. Definiciones")]
		public double P180CuerpoMin { get; set; }

		[Range(0.05, 0.5)]
		[Display(Name = "180: zona de cierre (fracción del rango)", Order = 7, GroupName = "3. Definiciones")]
		public double P180ZonaCierre { get; set; }

		[Range(0.3, 1.5)]
		[Display(Name = "180: retroceso de la 2ª vela (fracción)", Description = "La 2ª vela cierra recuperando al menos esta fracción del rango de la 1ª. 2/3 = entrada 2/3 del manual; 1 = entrada 100 %.", Order = 16, GroupName = "3. Definiciones")]
		public double P180Retroceso { get; set; }

		[Display(Name = "RBI / GBI: confirmar por cierre", Description = "Activado: la vela 3 o 4 debe cerrar más allá del extremo de la vela ignorada. Desactivado (manual): basta con que opere más allá.", Order = 19, GroupName = "3. Definiciones")]
		public bool IgnPorCierre { get; set; }

		[Range(0.0, 1.0)]
		[Display(Name = "RBI / GBI: cuerpo mínimo de la vela 1 (fracción del rango)", Order = 20, GroupName = "3. Definiciones")]
		public double IgnCuerpoV1 { get; set; }

		[Display(Name = "RBI / GBI: exigir zona de la vela 1", Description = "La vela ignorada queda en la zona superior (RBI) o inferior (GBI) de la vela 1, sin superar su extremo.", Order = 21, GroupName = "3. Definiciones")]
		public bool IgnTercio { get; set; }

		[Range(0.1, 1.0)]
		[Display(Name = "RBI / GBI: zona de la vela 1 (fracción del rango)", Description = "1/3 = tercio superior (inferior) del manual.", Order = 22, GroupName = "3. Definiciones")]
		public double IgnZona { get; set; }

		[Display(Name = "RBI / GBI: exigir tendencia (20mm)", Description = "La Media 2 sube (RBI) o baja (GBI) y la vela ignorada queda sobre (bajo) ella.", Order = 23, GroupName = "3. Definiciones")]
		public bool IgnTendencia { get; set; }

		[Range(0.3, 1.0)]
		[Display(Name = "BT / TT: cola mínima (fracción del rango)", Order = 9, GroupName = "3. Definiciones")]
		public double ColaMin { get; set; }

		[Range(0.1, 1.5)]
		[Display(Name = "GBR / RBR: cuerpo anterior recuperado (fracción)", Description = "La vela de reversa cierra recuperando al menos esta fracción del cuerpo de la vela anterior. 1 = cierra más allá de su apertura.", Order = 12, GroupName = "3. Definiciones")]
		public double ReversaMin { get; set; }

		// --- 5. Ubicación (cap. 12) ---
		[Range(0.0, 5.0)]
		[Display(Name = "Distancia al nivel (ATR 14)", Description = "Piso: el mínimo llega a esta distancia del nivel o lo cruza, y el cierre no queda más lejos que esto por debajo. Techo: el espejo.", Order = 2, GroupName = "5. Ubicación")]
		public double DistUbicAtr { get; set; }

		[Display(Name = "20mm y 8mm con pendiente", Description = "Media 2 o Media 1 subiendo (piso) o bajando (techo).", Order = 3, GroupName = "5. Ubicación")]
		public bool UbicMedias { get; set; }

		[Range(1, 50)]
		[Display(Name = "Medias: velas para la pendiente", Order = 4, GroupName = "5. Ubicación")]
		public int Pendiente20mm { get; set; }

		[Display(Name = "200mm plana", Description = "Media 3 casi plana.", Order = 5, GroupName = "5. Ubicación")]
		public bool Ubic200 { get; set; }

		[Range(0.0, 10.0)]
		[Display(Name = "200mm plana: cambio máximo (ATR 14)", Order = 6, GroupName = "5. Ubicación")]
		public double Plana200Atr { get; set; }

		[Range(1, 200)]
		[Display(Name = "200mm plana: velas", Order = 7, GroupName = "5. Ubicación")]
		public int Plana200Velas { get; set; }

		[Display(Name = "Máximos y mínimos anteriores (PHR / PLS)", Description = "Los 10 pivotes más recientes de cada tipo, rotos o no.", Order = 8, GroupName = "5. Ubicación")]
		public bool UbicPivotes { get; set; }

		[Range(1, 50)]
		[Display(Name = "Pivote: velas a cada lado", Order = 9, GroupName = "5. Ubicación")]
		public int PivoteVelas { get; set; }

		[Display(Name = "Cierre del día anterior (PDC)", Description = "Último cierre a las 16:00 NY o antes.", Order = 10, GroupName = "5. Ubicación")]
		public bool UbicPdc { get; set; }

		[Display(Name = "Retroceso del último tramo", Description = "Piso: la vela llega a la zona de retroceso del tramo desde el último mínimo pivote hasta el máximo posterior. Techo: el espejo.", Order = 11, GroupName = "5. Ubicación")]
		public bool UbicRetroceso { get; set; }

		[Range(0.0, 1.0)]
		[Display(Name = "Retroceso mínimo (fracción)", Order = 12, GroupName = "5. Ubicación")]
		public double RetrocesoMin { get; set; }

		[Range(0.0, 1.0)]
		[Display(Name = "Retroceso máximo (fracción)", Order = 13, GroupName = "5. Ubicación")]
		public double RetrocesoMax { get; set; }

		[Display(Name = "Extensión de la 20mm", Description = "El mínimo (piso) o el máximo (techo) a esta distancia o más de la Media 2.", Order = 14, GroupName = "5. Ubicación")]
		public bool UbicExtension { get; set; }

		[Range(0.5, 20.0)]
		[Display(Name = "Extensión mínima (ATR 14)", Order = 15, GroupName = "5. Ubicación")]
		public double ExtensionAtr { get; set; }

		[Display(Name = "Dibujar línea del PDC", Order = 16, GroupName = "5. Ubicación")]
		public bool VerPdc { get; set; }

		[XmlIgnore] [Display(Name = "Color del PDC", Order = 20, GroupName = "5. Ubicación")] public Brush ColorPdc { get; set; }
		[Browsable(false)] public string ColorPdcS { get { return Serialize.BrushToString(ColorPdc); } set { ColorPdc = Serialize.StringToBrush(value); } }

		[Range(0, 20)]
		[Display(Name = "Pivotes vigentes a dibujar (por tipo)", Description = "Máximos y mínimos sin romper que se dibujan: los más recientes de cada tipo. Los ya rotos se dibujan hasta su ruptura.", Order = 21, GroupName = "5. Ubicación")]
		public int PivotesVigentes { get; set; }

		[Display(Name = "Dibujar máximos y mínimos anteriores", Description = "Línea desde cada pivote hasta que una vela cierra más allá.", Order = 17, GroupName = "5. Ubicación")]
		public bool VerPivotes { get; set; }

		[XmlIgnore] [Display(Name = "Color máximo anterior", Order = 18, GroupName = "5. Ubicación")] public Brush ColorPivoteAlto { get; set; }
		[Browsable(false)] public string ColorPivoteAltoS { get { return Serialize.BrushToString(ColorPivoteAlto); } set { ColorPivoteAlto = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Color mínimo anterior", Order = 19, GroupName = "5. Ubicación")] public Brush ColorPivoteBajo { get; set; }
		[Browsable(false)] public string ColorPivoteBajoS { get { return Serialize.BrushToString(ColorPivoteBajo); } set { ColorPivoteBajo = Serialize.StringToBrush(value); } }

		[Range(0.05, 1.0)]
		[Display(Name = "NRB: rango máximo (x rango medio)", Order = 10, GroupName = "3. Definiciones")]
		public double NrbMax { get; set; }

		[Range(0.01, 1.0)]
		[Display(Name = "NB: cuerpo máximo (x cuerpo medio)", Order = 11, GroupName = "3. Definiciones")]
		public double NbMax { get; set; }

		// --- 4. Paleta ---
		[XmlIgnore] [Display(Name = "Elephant alcista", Order = 1, GroupName = "4. Colores")] public Brush ColorEbAlc { get; set; }
		[Browsable(false)] public string ColorEbAlcS { get { return Serialize.BrushToString(ColorEbAlc); } set { ColorEbAlc = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Elephant bajista", Order = 2, GroupName = "4. Colores")] public Brush ColorEbBaj { get; set; }
		[Browsable(false)] public string ColorEbBajS { get { return Serialize.BrushToString(ColorEbBaj); } set { ColorEbBaj = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Toro 180", Order = 3, GroupName = "4. Colores")] public Brush ColorToro180 { get; set; }
		[Browsable(false)] public string ColorToro180S { get { return Serialize.BrushToString(ColorToro180); } set { ColorToro180 = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Oso 180", Order = 4, GroupName = "4. Colores")] public Brush ColorOso180 { get; set; }
		[Browsable(false)] public string ColorOso180S { get { return Serialize.BrushToString(ColorOso180); } set { ColorOso180 = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Cola de piso (BT)", Order = 5, GroupName = "4. Colores")] public Brush ColorBt { get; set; }
		[Browsable(false)] public string ColorBtS { get { return Serialize.BrushToString(ColorBt); } set { ColorBt = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Cola de techo (TT)", Order = 6, GroupName = "4. Colores")] public Brush ColorTt { get; set; }
		[Browsable(false)] public string ColorTtS { get { return Serialize.BrushToString(ColorTt); } set { ColorTt = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Verde de reversa (GBR)", Order = 7, GroupName = "4. Colores")] public Brush ColorGbr { get; set; }
		[Browsable(false)] public string ColorGbrS { get { return Serialize.BrushToString(ColorGbr); } set { ColorGbr = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Roja de reversa (RBR)", Order = 8, GroupName = "4. Colores")] public Brush ColorRbr { get; set; }
		[Browsable(false)] public string ColorRbrS { get { return Serialize.BrushToString(ColorRbr); } set { ColorRbr = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Roja ignorada (RBI)", Order = 9, GroupName = "4. Colores")] public Brush ColorRbi { get; set; }
		[Browsable(false)] public string ColorRbiS { get { return Serialize.BrushToString(ColorRbi); } set { ColorRbi = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Verde ignorada (GBI)", Order = 10, GroupName = "4. Colores")] public Brush ColorGbi { get; set; }
		[Browsable(false)] public string ColorGbiS { get { return Serialize.BrushToString(ColorGbi); } set { ColorGbi = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Rango estrecho de piso (NRB)", Order = 11, GroupName = "4. Colores")] public Brush ColorNrbPiso { get; set; }
		[Browsable(false)] public string ColorNrbPisoS { get { return Serialize.BrushToString(ColorNrbPiso); } set { ColorNrbPiso = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Rango estrecho de techo (NRB)", Order = 12, GroupName = "4. Colores")] public Brush ColorNrbTecho { get; set; }
		[Browsable(false)] public string ColorNrbTechoS { get { return Serialize.BrushToString(ColorNrbTecho); } set { ColorNrbTecho = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Cuerpo estrecho de piso (NB)", Order = 13, GroupName = "4. Colores")] public Brush ColorNbPiso { get; set; }
		[Browsable(false)] public string ColorNbPisoS { get { return Serialize.BrushToString(ColorNbPiso); } set { ColorNbPiso = Serialize.StringToBrush(value); } }

		[XmlIgnore] [Display(Name = "Cuerpo estrecho de techo (NB)", Order = 14, GroupName = "4. Colores")] public Brush ColorNbTecho { get; set; }
		[Browsable(false)] public string ColorNbTechoS { get { return Serialize.BrushToString(ColorNbTecho); } set { ColorNbTecho = Serialize.StringToBrush(value); } }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OliverVelezPatrones[] cacheOliverVelezPatrones;
		public OliverVelezPatrones OliverVelezPatrones(OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			return OliverVelezPatrones(Input, ma1Tipo, ma1Periodo, ma2Tipo, ma2Periodo, ma3Tipo, ma3Periodo);
		}

		public OliverVelezPatrones OliverVelezPatrones(ISeries<double> input, OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			if (cacheOliverVelezPatrones != null)
				for (int idx = 0; idx < cacheOliverVelezPatrones.Length; idx++)
					if (cacheOliverVelezPatrones[idx] != null && cacheOliverVelezPatrones[idx].Ma1Tipo == ma1Tipo && cacheOliverVelezPatrones[idx].Ma1Periodo == ma1Periodo && cacheOliverVelezPatrones[idx].Ma2Tipo == ma2Tipo && cacheOliverVelezPatrones[idx].Ma2Periodo == ma2Periodo && cacheOliverVelezPatrones[idx].Ma3Tipo == ma3Tipo && cacheOliverVelezPatrones[idx].Ma3Periodo == ma3Periodo && cacheOliverVelezPatrones[idx].EqualsInput(input))
						return cacheOliverVelezPatrones[idx];
			return CacheIndicator<OliverVelezPatrones>(new OliverVelezPatrones(){ Ma1Tipo = ma1Tipo, Ma1Periodo = ma1Periodo, Ma2Tipo = ma2Tipo, Ma2Periodo = ma2Periodo, Ma3Tipo = ma3Tipo, Ma3Periodo = ma3Periodo }, input, ref cacheOliverVelezPatrones);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OliverVelezPatrones OliverVelezPatrones(OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			return indicator.OliverVelezPatrones(Input, ma1Tipo, ma1Periodo, ma2Tipo, ma2Periodo, ma3Tipo, ma3Periodo);
		}

		public Indicators.OliverVelezPatrones OliverVelezPatrones(ISeries<double> input , OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			return indicator.OliverVelezPatrones(input, ma1Tipo, ma1Periodo, ma2Tipo, ma2Periodo, ma3Tipo, ma3Periodo);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OliverVelezPatrones OliverVelezPatrones(OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			return indicator.OliverVelezPatrones(Input, ma1Tipo, ma1Periodo, ma2Tipo, ma2Periodo, ma3Tipo, ma3Periodo);
		}

		public Indicators.OliverVelezPatrones OliverVelezPatrones(ISeries<double> input , OvTipoMedia ma1Tipo, int ma1Periodo, OvTipoMedia ma2Tipo, int ma2Periodo, OvTipoMedia ma3Tipo, int ma3Periodo)
		{
			return indicator.OliverVelezPatrones(input, ma1Tipo, ma1Periodo, ma2Tipo, ma2Periodo, ma3Tipo, ma3Periodo);
		}
	}
}

#endregion
