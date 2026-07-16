import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { JourneyBarComponent } from './shared/journey-bar/journey-bar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, JourneyBarComponent],
  templateUrl: './app.html',
  styleUrls: ['./app.css']
})
export class AppComponent {
  title = 'soea-angular';
}
