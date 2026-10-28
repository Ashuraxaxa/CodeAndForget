import type { Messages } from '.'

/**
 * El español es el texto del código; aquí van solo las formas en singular de los textos con cantidad ({n}),
 * para no mostrar "1 registros".
 */
export const es: Messages = {
  '{n} cambios en el esquema': { one: '{n} cambio en el esquema', other: '{n} cambios en el esquema' },
  '{n} ajustes sin efecto': { one: '{n} ajuste sin efecto', other: '{n} ajustes sin efecto' },
  '{n} cambios': { one: '{n} cambio', other: '{n} cambios' },
  '{n} tablas y vistas': { one: '{n} tabla o vista', other: '{n} tablas y vistas' },
  'Conexión correcta: {n} tablas/vistas encontradas.': { one: 'Conexión correcta: {n} tabla/vista encontrada.', other: 'Conexión correcta: {n} tablas/vistas encontradas.' },
  '{n} filas': { one: '{n} fila', other: '{n} filas' },
  '{n} válidas': { one: '{n} válida', other: '{n} válidas' },
  '{n} nuevas': { one: '{n} nueva', other: '{n} nuevas' },
  '{n} se actualizan': { one: '{n} se actualiza', other: '{n} se actualizan' },
  '{n} errores': { one: '{n} error', other: '{n} errores' },
  'Primeras {n} filas, como se van a guardar:': { one: 'Primera fila, como se va a guardar:', other: 'Primeras {n} filas, como se van a guardar:' },
  'Importar {n} filas': { one: 'Importar {n} fila', other: 'Importar {n} filas' },
  'Se importaron {n} registros.': { one: 'Se importó {n} registro.', other: 'Se importaron {n} registros.' },
  '+{n} tablas': { one: '+{n} tabla', other: '+{n} tablas' },
  '{n} registros': { one: '{n} registro', other: '{n} registros' },
}
