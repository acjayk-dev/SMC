> Nota de referencias: `Referencia/manual.md` no conserva la numeración de páginas del PDF (solo marcadores sueltos: pág. 111 = Cap. 9, pág. 128-129 = Cap. 10/10.1, pág. 166 = Cap. 11, pág. 172 = Cap. 11.2, pág. 181 = Cap. 11.4). Por eso las citas usan el **capítulo** del manual. Etiquetas: [PROPUESTO] = valor no definido en el manual; [NO ENCONTRADO] = regla sin respaldo textual en el manual.

## 1. Definiciones de Patrones y Conceptos (Criterios Medibles)

- Vela Elefante (Elephant Bar / WRB): Vela de cuerpo sólido relativamente grande en comparación con las velas anteriores; debe ser obvia (Cap. 1: "WRB velas de cuerpo amplio"; la fuerza la da "la distancia entre la apertura y el cierre"). Criterio codificable, sobre el **cuerpo** y no sobre el rango: `Cuerpo_Vela > 1.5 * ta.sma(Cuerpo_Vela, 20)` y cuerpo sólido `Cuerpo_Vela >= 0.5 * Rango_Vela` [PROPUESTO]. Control (Cap. 1, Regla de 2/3): la vela ideal es grande, sólida y sin mechas. El control se considera **débil** si se borra ~1/2 del color (cuerpo) y **perdido** si se borra 2/3 o más del color. Criterio codificable: cierre en el 1/3 superior para Toros (`Close >= High - Rango * 0.333`) o 1/3 inferior para Osos (`Close <= Low + Rango * 0.333`) [PROPUESTO, derivado de la regla 2/3]. Si aparece al inicio de un movimiento es de **ignición** (se espera seguimiento); si aparece tras un movimiento extendido es de **agotamiento** (Cap. 1).
- Cola de Piso (Bottom Tail - BT): Vela cuya cola inferior representa 2/3 o más del rango total ((math.min(open, close) - low) / (high - low) >= 0.667). Señal más fuerte tras un descenso constante de 3 a 5 velas (Cap. 11.4, pág. 179-181).
- Cola de Techo (Top Tail - TT): Vela cuya cola superior representa 2/3 o más del rango total ((high - math.max(open, close)) / (high - low) >= 0.667). Señal más fuerte tras un rally constante de 3 a 5 velas (Cap. 11.4, pág. 179-181).
- Vela Verde / Roja de Reversa (GBR / RBR) (Cap. 11.1): vela verde (roja) que retrocede la vela roja (verde) anterior. Criterio codificable, con la regla 2/3 del Cap. 1 ("la siguiente vela borra 2/3 o más del color de la vela anterior"): GBR `isGreen and isRed[1] and Close >= Close_1 + Cuerpo_1 * 0.667`; RBR `isRed and isGreen[1] and Close <= Close_1 - Cuerpo_1 * 0.667`. Más fuerte tras 3 a 5 velas en contra. Entrada al retroceder la vela anterior (con TIF) o al violar su máximo/mínimo; stop bajo/sobre la vela de entrada o la anterior.
- Vela de Rango Estrecho (NRB) (Cap. 11.3) / Cuerpo Estrecho (NB) (Cap. 11.2, pág. 172): Vela de rango total (NRB) o cuerpo (NB) pequeño. Criterio codificable: `Rango_Vela < 0.5 * ta.sma(Rango_Vela, 20)` [PROPUESTO]. Tras una caída/subida de 3 a 5 velas las probabilidades de un movimiento explosivo aumentan (filtro de calidad, no requisito).
- Toro 180 (+180 / Bull 180) (Cap. 17.1): Vela 1 muy bajista (la mayoría del rango es rojo, apertura en la parte superior y cierre en el piso del rango); entre más lejos de la 20mm, mejor. Entrada: $0.01 sobre el máximo de la Vela 1 (método 100%: High > High_1). Alternativa 2/3: entrar cuando la Vela 2 recupera 2/3 o más del rango de la Vela 1 (`Close >= High_1 - Rango_1 * 0.333`), **solo si había más velas rojas antes de la Vela 1**; si la Vela 1 es la única roja, usar obligatoriamente el método 100%. Entre 2/3 y 100% aplican reglas TIF; al superar High_1 se puede entrar sin TIF.
- Oso 180 (-180 / Bear 180) (Cap. 17.1): Vela 1 muy alcista (la mayoría del rango es verde, apertura en la parte inferior y cierre en el techo del rango); entre más lejos de la 20mm, mejor. Entrada: $0.01 bajo el mínimo de la Vela 1 (método 100%: Low < Low_1). Alternativa 2/3 (`Close <= Low_1 + Rango_1 * 0.333`) **solo si había más velas verdes antes de la Vela 1**; si es la única verde, usar el método 100%. Entre 2/3 y 100% aplican reglas TIF; al romper Low_1 se puede entrar sin TIF.
- Vela Roja Ignorada (RBI) (Cap. 17.2): Vela 1 verde alcista (dicta la dirección); Vela 2 roja. Ideal (no requerido): la Vela 2 se mantiene en el 1/3 superior de la Vela 1 sin superar su máximo (`High_2 <= High_1 y Low_2 >= High_1 - Rango_1 * 0.333`). Gatillo: la Vela 3 (o 4) opera $0.01 por encima del máximo de la Vela 2 (High_3 > High_2). Mejor en 1, 2 y 5 min. Contexto: "ayuda al trader a entrar en tendencias poderosas que ya están en curso" (punto 4) → codificable: solo con tendencia alcista del Cap. 6 [PROPUESTO]. Gestión: trailing hasta el objetivo o hasta que "el mínimo de la vela de reverso se elimine"; sin salida por agotamiento.
- Vela Verde Ignorada (GBI) (Cap. 17.2): Vela 1 roja bajista (dicta la dirección); Vela 2 verde. Ideal (no requerido): la Vela 2 se mantiene en el 1/3 inferior de la Vela 1 sin superar su mínimo (`Low_2 >= Low_1 y High_2 <= Low_1 + Rango_1 * 0.333`). Gatillo: la Vela 3 (o 4) opera $0.01 por debajo del mínimo de la Vela 2 (Low_3 < Low_2). Mejor en 1, 2 y 5 min. Solo con tendencia bajista del Cap. 6 [PROPUESTO]. Gestión: trailing hasta el objetivo o hasta que "el máximo de la vela de reverso se elimine".
- Jugada de Rompimiento Alcista/Bajista (BOP / BDP) (Cap. 16): Base lateral ajustada y delgada (no amplia ni volátil), de velas pequeñas y con volumen liviano; 8mm y/o 20mm idealmente a favor (alcistas en BOP, descendentes en BDP — en BDP es aún más importante). Los mejores rompimientos ocurren en o cerca de la 8mm/20mm. Número mínimo de velas de la base: 3 [PROPUESTO]. Gatillo: vela que rompe el máximo/mínimo de la base. Pausa (base) vs techo: las pausas son estrechas, de velas pequeñas, volumen liviano y no cruzan mucho la 20mm (codificable: mínimo de la base ≥ 20mm − 0.2 × ATR [PROPUESTO]). El volumen liviano en la base es requisito ("debe ser liviano, no pesado"). Uso en 2 y 5 min.
- Compra de Caída Climática / Venta de Avance Climático (CDB / CAS) (Cap. 15): (1) 5 o más altos más bajos (CDB) / bajos más altos (CAS), o 5 o más velas rojas (CDB) / verdes (CAS) consecutivas — ambos a la vez es más potente; (2) una o más señales de reversa: BT, GBR, NRB o NB (CDB) / TT, RBR, NRB o NB (CAS); (3) lejos de la 20mm o de un área previa de soporte/resistencia (concepto visual; mejor si también lejos de la 8mm); (4) mayor volumen (no requerido); (5) hora de reversa (a diferencia del volumen, el manual no la marca como no requerida → se exige, con tolerancia de ± minutos [PROPUESTO]). Criterio de distancia codificable: `math.abs(close - SMA20) > 2.0 * ta.atr(14)` [PROPUESTO]. Gestión (Cap. 15): trailing vela a vela tras 2 velas "hasta que el precio objetivo sea alcanzado o una RBR mayor haya ocurrido" (GBR en CAS); "muchos iFundTraders optan por mover su stop a Break Even una vez tienen el avance de dos velas". Nota del manual: la primera configuración de venta (VSS) después de una caída climática, y la primera de compra (VBS) después de un avance climático, poseen bajas posibilidades.

## 2. Medias Móviles Usadas e Interpretación

## · Media Móvil Simple de 8 Periodos (8mm):

- o Tipo: Media Móvil Simple (ta.sma(close, 8)).
- o Interpretación: Captura y soporta los movimientos más poderosos. Sirve como base del trailing stop de momentum (Cap. 5, Cap. 9).

## · Media Móvil Simple de 20 Periodos (20mm / 21mm):

- o Tipo: Media Móvil Simple (ta.sma(close, 20)). Intercambiable con 21mm (Cap. 3, Cap. 5).
- o Interpretación: Revela la dirección de la tendencia, es soporte/resistencia (si está alcista/descendente), línea media en laterales (20mm plana) e imán: el precio no puede permanecer mucho tiempo extendido lejos de ella (Cap. 5).

## · Media Móvil Simple de 200 Periodos (200mm / "Goliat"):

- o Tipo: Media Móvil Simple (ta.sma(close, 200)).
- o Interpretación: Soporte/resistencia universal en todos los marcos de tiempo. Es más poderosa cuando está **plana** ("la planitud es el rey"), sobre todo si la 20mm está lejos. Imán: si el precio está muy lejos de la 20mm y la 20mm muy lejos de la 200mm, una gran reversa está cerca (Cap. 5).

## · Interpretación de Tendencias y Estados (Cap. 6):

- o Tendencia Alcista Regular: Close > SMA_20 con SMA_20 alcista (SMA_20 > SMA_20[1]).
- o Tendencia de Poder Alcista: Close > SMA_20 alcista y SMA_20 > SMA_200 (la pendiente de la 200mm no es requisito).
- o Súper Tendencia Alcista: Close > SMA_8 alcista y SMA_8 > SMA_20 alcista.
- o Tendencia Bajista Regular: Close < SMA_20 con SMA_20 descendente.
- o Tendencia de Poder Bajista: Close < SMA_20 descendente y SMA_20 < SMA_200.
- o Súper Tendencia Bajista: Close < SMA_8 descendente y SMA_8 < SMA_20 descendente (la 200mm no forma parte de la definición).
- o Estado Estrecho vs Amplio: Distancia entre 20mm y 200mm (math.abs(SMA_20 - SMA_200)). [NO ENCONTRADO] — el manual solo lo menciona como tema de video (Cap. 21, "De Estado Estrecho a Estado Amplio") sin definirlo.

## 3. Condiciones de Entrada (Long y Short)

- Entrada Long (Largo):
- o Gatillo Estándar: Orden stop de compra $0.01 por encima del máximo de la vela de señal/anterior (Buy_Stop = High_SignalBar + 0.01) (Cap. 10.1, 15, 16, 17). Si el máximo está muy lejos, se puede usar el máximo de la vela anterior en el marco de tiempo inferior (Cap. 10.1, 15).
- o Gatillo Intra-vela (TIF): Comprar cuando la vela en formación recupera 2/3 o más del rango de referencia (`Close >= High_Ref - Rango_Ref * 0.333`) respetando las restricciones TIF; si ya eliminó el máximo de la vela anterior, se ignora el TIF (Cap. 11.4, 17.1).
- o Momentum (Cap. 10.3): comprar al superar el máximo de una vela de ignición (similar a Control Absoluto), con stop bajo su mínimo; evitar resistencia inmediata encima ("no queremos comprar justo frente a una resistencia inmediata o muy cercana"). Codificación [PROPUESTO]: sin ninguna resistencia del Cap. 12 entre la entrada y 1 × riesgo por encima.
- o Vigencia de la señal de piso (Cap. 11.1-11.4): con BT, GBR, NB o NRB se compra también "la próxima vez que se viole el máximo de una vela anterior". Codificación: la señal sigue armada 3 velas más [PROPUESTO] con el gatillo en el máximo de la vela anterior, y se anula si se pierde el mínimo de la vela de señal.
- o Señal de piso en la VBS (ambigüedad resuelta): el Cap. 10.1 la lista entre los "complementos" de la VBS, mientras que el Cap. 11.5 dice "sólo comprará una caída de 3 a 5 velas si va acompañada de una de las 7 señales de reversa". Prevalece el Cap. 10.1 por ser la regla específica de la VBS: la señal de piso es complemento, no requisito. El requisito del Cap. 11.5 se aplica a la Reversa 3-5 (Ley #3). Además, el texto solo nombra 4 de las 7 señales (BT, GBR, NRB, NB) [NO ENCONTRADO: las otras 3 están en imágenes].

## · Entrada Short (Corto):

- o Gatillo Estándar: Orden stop de venta $0.01 por debajo del mínimo de la vela de señal/anterior (Sell_Stop = Low_SignalBar - 0.01) (Cap. 10.1, 15, 16, 17).
- o Gatillo Intra-vela (TIF): Vender cuando la vela en formación pierde 2/3 o más del rango de referencia (`Close <= Low_Ref + Rango_Ref * 0.333`) respetando las restricciones TIF; si ya eliminó el mínimo de la vela anterior, se ignora el TIF (Cap. 11.4, 17.1).
- o Momentum (Cap. 10.3): vender al romper el mínimo de una vela de ignición, con stop sobre su máximo; evitar soporte inmediato debajo.

## 4. Stops, Objetivos y Trailing

- Stop Loss Inicial (Stop por Evento — usado ~70% de las veces) (Cap. 8, 8.1):
- o Largo: $0.01 por debajo del mínimo de la vela de entrada; si está muy cerca, el mínimo de la vela anterior (Stop_Loss = Low_EntryBar - 0.01) (Cap. 10.1, 15, 17).
- o Corto: $0.01 por encima del máximo de la vela de entrada; si está muy cerca, el máximo de la vela anterior (Stop_Loss = High_EntryBar + 0.01) (Cap. 10.1, 15, 17).
- o BOP: $0.01 bajo la vela de rompimiento. BDP: $0.01 sobre la vela de rompimiento o sobre la base completa (a elección) (Cap. 16).
- o NRB: "Los stops se colocan justo por debajo del mínimo del NRB" (sobre el máximo en cortos) cuando la señal de piso/techo es un NRB (Cap. 11.3).
- o Stop Alternativo de 2/3: si el stop por evento está muy lejos, usar el nivel de retroceso de 2/3 de la vela (Largo: `Stop_Loss = High_EB - Rango_EB * 0.667 - 0.01`) (Cap. 8.1, 17.1). En Momentum se mide sobre la Elefante de ignición.
- o Pérdida Máxima por Trade (MLPT — usado ~25% de las veces): stop monetario fijo cuando el stop por evento quedaría en un lugar inapropiado (p.ej. en la 200mm). MLPT entre el 25% y el 50% de la Pérdida Máxima Diaria (MLPD) (Cap. 8, 8.2).

## · Objetivos de Ganancia (Take Profit):

- o T1: Nivel del 50% de retroceso del movimiento previo (VBS/VSS) (Cap. 10.1, pág. 129-131). En jugadas climáticas: objetivo mínimo la 20mm o el 50% de retroceso (Cap. 15).
- o T2: En o cerca del pivote máximo/mínimo anterior (Target_2 = High_PriorSwing). Una media móvil cercana también puede ser objetivo (Cap. 10.1).
- o T3: Nuevo máximo/mínimo (por encima/debajo del pivote anterior) (Cap. 10.1). En BOP/BDP, si no hay referencia previa: proyectar la altura de la base (Target_3 = Breakout + Base_Height) (Cap. 16).
- o Porcentaje de la posición a cerrar en cada objetivo: [NO ENCONTRADO]. 50% en T1 [PROPUESTO].

## · Trailing Stop:

- o Break-Even: mover el stop al precio de entrada cuando el trade alcanza 1:1 riesgo/beneficio (Stop_Loss = Entry_Price cuando la ganancia >= riesgo inicial) (Cap. 22, Glosario).
- o Vela a Vela (Bar-by-Bar) (Cap. 9, pág. 111): tras un avance/caída de 2 velas a favor (incluye la vela de entrada si cierra a favor del precio de entrada), mover el stop $0.01 bajo el mínimo de la vela anterior (Largos) o sobre el máximo de la vela anterior (Cortos) en cada nueva vela. En Momentum basta 1 vela a favor (Cap. 10.3). Salir/dejar de ajustar cuando: se toca el objetivo, se forma una cola en contra (TT/BT), una vela de cuerpo amplio a favor (agotamiento), o una vela contraria (RBR/GBR) elimina más del 50% de la vela anterior (Cap. 10.1). Estas salidas son las de la VBS/VSS y se usan por defecto. Los setups con reglas propias son: CDB/CAS, que sale solo con el objetivo o con una RBR/GBR mayor y opcionalmente pasa a break-even tras 2 velas (Cap. 15); y RBI/GBI, que sale con el objetivo o cuando se elimina el mínimo/máximo de la vela de reverso, es decir, con el propio trailing (Cap. 17.2).
- o Trailing 8mm (Momentum): mantener la posición mientras el precio se mantenga por encima (Largos) / por debajo (Cortos) de la 8mm. Uso en 1, 2 y 5 min (Cap. 9). Confirmación por cierre (Close < SMA_8 invalida Largos) [PROPUESTO].
- o Trailing 20mm: mantener mientras el precio se mantenga del lado correcto de la 20mm; solo en tendencia, mejor en 2 y 5 min (Cap. 9).

## 5. Filtros (Timeframe, Sesión, Tendencia, Volumen)

- Timeframe: Gráficos operativos de 1, 2 y 5 min (5 min es el principal). Marcos analíticos: 15 min, 60 min y Diario (Cap. 3, Cap. 4).
- Sesión y Restricción TIF (Time In Formation) (Cap. 11.4, pág. ~181-182):
- o 1 min y 2 min: no hay entrada temprana; la vela debe estar completada.
- o 5 min: entrada temprana solo si la vela lleva >= 4:52 de formación.
- o 15 min: entrada temprana solo si la vela lleva >= 14:30 de formación.
- o 60 min: entrada temprana solo si la vela lleva >= 57:00 de formación.
- o Horarios de Reversa (ET) (Cap. 10.1, 13, 14, 15): 9:35 (micro), 10:00, 10:30, 11:15-11:30, 12:00, 13:30, 14:15-14:30, 15:00, 15:30. Los más importantes: 10:00, 11:15-11:30 y 14:15-14:30.
- o Filtro de Cierre de Mercado: cancelar nuevas entradas cerca del final de la sesión [NO ENCONTRADO] [PROPUESTO].

## · Filtro de Tendencia y Ubicación (Location Filter):

- o Ubicación 1: Operaciones a favor de tendencia en o cerca de la 8mm/20mm alcista (compras) o descendente (ventas) (`math.abs(low - SMA20) <= 0.2 * ta.atr(14)`) [PROPUESTO] (Cap. 5, 10.1, 16). VBS: precio sobre la 20mm (20mm sobre 200mm ideal, no requerido) tras 3 o más velas consecutivas con alto más bajo **y** bajo más bajo, o 3 o más velas rojas (2 en tendencias muy poderosas); VSS a la inversa (bajos más altos y altos más altos, o velas verdes) (Cap. 10.1). Ubicaciones: soporte de precio, de medias móviles, de retroceso y cierre del día anterior. En el Cap. 10.1 son complementos, pero las Reglas #1-#4 del Cap. 12 dicen "Limita todas tus compras a algún área de soporte". Codificación: VBS/VSS y Reversa 3-5 exigen cualquiera de las 8 formas de soporte/resistencia del Cap. 12 (CDB/CAS ya la cumple con la forma 8, "muy por debajo de la 20mm").
- o Ubicación 2 (Climática): operaciones contratendencia solo con precio lejos de la 20mm (y de la 8mm) tras 5+ velas consecutivas de un solo color o 5+ altos/bajos consecutivos, con señal de reversa (Cap. 6, 15).

## · Filtro de Volumen:

- o Volumen mayor en la vela de señal: **opcional**, no es requisito (Cap. 10.1, 15). Criterio codificable: volume > ta.sma(volume, 20) [PROPUESTO], usar solo como confirmación, no como filtro obligatorio.
- o BOP/BDP: volumen liviano durante la base; el volumen alto caracteriza techos/pisos, no pausas (Cap. 16).

## · Soporte y Resistencia (Cap. 12):

- o Principio: los soportes y resistencias "no son precios específicos, son áreas; cuando el precio va a ellas o cerca es suficiente". Reglas de compra y de corto #1-#4: limitar las compras a un área de soporte (y las ventas a una de resistencia) donde se forme una de las 4 señales de piso (techo).
- o Formas de soporte (resistencia a la inversa): (1) máximo anterior (a menudo coincide con el 50% de retroceso); (2) mínimo anterior (retroceso del 100%, "el poder de los dobles"); (3) 8mm/20mm alcista; (4) 200mm plana bajo el precio; (5) retroceso del 50%-55% (≈ 1/2 de la ganancia); (6) retroceso del 33%; (7) cierre del día anterior (PDC); (8) muy por debajo de la 20mm.
- o Codificación: zonas en pivotes confirmados (5 velas a cada lado [PROPUESTO]) con grosor de 0.3 × ATR [PROPUESTO]; al cerrar al otro lado cambian de rol (máximo anterior → soporte) y a la segunda ruptura se eliminan [PROPUESTO]. Retrocesos 33/50/55% del último tramo entre pivotes. 200mm plana: se mueve menos de 0.5 × ATR en 20 velas [PROPUESTO]. Cercanía: 0.2 × ATR [PROPUESTO].
- o Línea de tendencia: el manual solo dice que "la línea de tendencia bajista suele coincidir con la resistencia 20mm" (Cap. 12) y no indica cómo trazarla. Codificación [PROPUESTO]: une los dos últimos pivotes con bajo más alto (soporte) o alto más bajo (resistencia), según la definición de tendencia del Cap. 6, **solo si la 20mm tiene la misma pendiente entre ambos pivotes**. Se rompe al cerrar al otro lado por más de 0.2 × ATR.

## 6. Reglas Discrecionales (No Codificables)

- Reconocimiento visual "obvio" de la Vela Elefante: "está ahí o no está, ellas no se esconden" (Cap. 1).
- Evaluación de "Full Control" vs "Buen Control" vs "Control Débil": juicio visual sin medición ("no requiere que se mida o calcule") (Cap. 1).
- Apreciación visual de "lejos de la 20mm" en jugadas climáticas ("concepto visual") (Cap. 15).
- Calidad de consistencia del gráfico: "sin consistencia" = mezcla de velas rojas y verdes sin continuidad; no operar (Cap. 22, Glosario).
- Ignición vs agotamiento de la Vela Elefante según su posición en el movimiento (Cap. 1).
- Evaluación visual del estado estrecho/amplio de las medias [NO ENCONTRADO] (solo título de video, Cap. 21).
- Lectura del Nivel 2 y contexto del mercado general (SPY) [NO ENCONTRADO].

## 7. Parámetros Sugeridos para Pine Script

| Parámetro         | Valor en el manual            | Valor Sugerido   | Descripción                                            |
|-------------------|-------------------------------|------------------|--------------------------------------------------------|
| p_sma_fast        | 8 (Cap. 5)                    | 8                | Periodo de Media Móvil Rápida                          |
| p_sma_med         | 20 o 21 (Cap. 3, 5)           | 20               | Periodo de Media Móvil Media                           |
| p_sma_slow        | 200 (Cap. 5)                  | 200              | Periodo de Media Móvil Lenta ("Goliat")                |
| p_eb_mult         | No especificado               | 1.5 [PROPUESTO]  | Multiplicador de SMA del cuerpo para Vela Elefante     |
| p_eb_body         | "Cuerpo sólido" (Cap. 1)      | 0.5 [PROPUESTO]  | Cuerpo mínimo de la Elefante (fracción del rango)      |
| p_hold_bars       | No especificado (Cap. 11)     | 3 [PROPUESTO]    | Velas extra en que la señal de piso/techo sigue armada |
| p_mom_room        | No especificado (Cap. 10.3)   | 1.0 [PROPUESTO]  | Espacio libre mínimo sobre la entrada de Momentum (R)  |
| p_nrb_mult        | No especificado               | 0.5 [PROPUESTO]  | Multiplicador para Vela de Rango Estrecho              |
| p_tail_pct        | 2/3 (Cap. 11.4, pág. 179-181) | 0.667            | Proporción mínima de cola en Bottom/Top Tails          |
| p_control_pct     | 2/3 (Cap. 1)                  | 0.667            | Retroceso del color que marca pérdida de control       |
| p_weak_pct        | 1/2 (Cap. 1)                  | 0.5              | Retroceso del color que marca control débil            |
| p_trend_bars      | 3 (Cap. 10.1)                 | 3                | Mínimo de velas de retroceso para VBS/VSS              |
| p_climatic_bars   | 5 (Cap. 15)                   | 5                | Mínimo de velas consecutivas para jugadas climáticas   |
| p_climatic_dist   | No especificado               | 2.0 [PROPUESTO]  | Distancia en ATRs a la 20mm para considerar "lejos"    |
| p_base_bars       | No especificado               | 3 [PROPUESTO]    | Mínimo de velas de la base en BOP/BDP                  |
| p_stop_offset     | $0.01 (Cap. 10.1, 15, 17)     | 0.01             | Offset en dólares por debajo/encima del extremo        |
| p_mlpt_pct        | 25%-50% de MLPD (Cap. 8)      | 0.5              | MLPT como fracción de la pérdida máxima diaria         |
| p_tif_5min        | 4:52 (Cap. 11.4)              | 292 (segundos)   | Tiempo de formación mínimo en gráficos de 5 min        |
| p_tif_15min       | 14:30 (Cap. 11.4)             | 870 (segundos)   | Tiempo de formación mínimo en gráficos de 15 min       |
| p_tif_60min       | 57:00 (Cap. 11.4)             | 3420 (segundos)  | Tiempo de formación mínimo en gráficos de 60 min       |
