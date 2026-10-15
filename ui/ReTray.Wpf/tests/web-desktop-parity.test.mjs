import { test } from 'node:test';
import assert from 'node:assert/strict';
import { desktopRound } from '../src/LanDeviceMonitor.App/Web/precision.js';
import { dateMarkup, clock, relativeDate, relativeTime, bytes } from '../src/LanDeviceMonitor.App/Web/ui.js';

test('fractional DPI uses desktop midpoint-to-even grid rounding',()=>{
  assert.equal(desktopRound(1,1.25),.8);
  assert.equal(desktopRound(2,1.25),1.6);
  assert.equal(desktopRound(3,1.25),3.2);
  assert.equal(desktopRound(14,1.25),14.4);
  assert.equal(desktopRound(17,1.25),16.8);
  assert.equal(desktopRound(-2.5,1),-2);
  assert.equal(desktopRound(-3.5,1),-4);
});
test('date blanks and range separator preserve the accepted desktop spacing',()=>{
  const markup=dateMarkup('07\u2009月\u200923\u2009日 ~ 10\u2009月\u200905\u2009日');
  assert.equal((markup.match(/class="date-gap"/g)||[]).length,6);
  assert.equal((markup.match(/class="range-gap"/g)||[]).length,2);
  assert.ok(!markup.includes('\u2009'));
});
test('server date and clock retain the desktop offset rather than changing date locally',()=>{
  const now='2026-10-05T00:20:00+08:00';
  assert.equal(clock(now),'00:20:00');
  assert.equal(relativeDate('2026-10-04T23:40:00+08:00',now),'昨天');
  assert.equal(relativeTime(null,now),'时间未知');
  assert.equal(relativeTime('2026-10-04T23:40:00+08:00',now),'40\u2009分钟前');
});
test('foreground metric text uses the desktop three-significant-digit contract',()=>{
  assert.deepEqual(bytes(234567,true),{value:'229',unit:'KB/s'});
  assert.deepEqual(bytes(67891234),{value:'64.7',unit:'MB'});
  assert.deepEqual(bytes(0),{value:'0.00',unit:'B'});
});
