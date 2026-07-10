import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient } from '@angular/common/http';
import { provideCharts } from 'ng2-charts';
import { BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip } from 'chart.js';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideAnimationsAsync(),
    provideHttpClient(),
    provideCharts({ registerables: [BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip] })
  ]
};
