function menu::vote(%cl) {
	%waiting = $Server::TourneyMode && (!$CountdownStarted && !$matchStarted);
	
	menu::new("Options", "vote", %cl);
	menu::add("Change Teams", "changeteams", %cl, (!$loadingMission) && (!$matchStarted || !$Server::TourneyMode) );
	menu::add("Vote to change mission", "vChangeMission", %cl);
    // Only show if routes are loaded
    if ($Route::NumLoadedRoutes > 0) { 
        menu::add("Select Route Bot...", "selectRouteBot", %cl);
    }
	menu::add("Vote to enter FFA mode", "vcffa", %cl, $Server::TourneyMode );
	menu::add("Vote to start the match", "vsmatch", %cl, %waiting );
	menu::add("Vote to enter Tournament mode", "vctourney", %cl, !$Server::TourneyMode );
	menu::add("Admin Options...", "adminoptions", %cl, (%cl.adminLevel > 0) );
}

function processMenuVote( %cl, %selection ) {
	if(%selection == "changeteams") {
         menu::changeteams( %cl );
		 return;
	}
    else if (%selection == "selectRouteBot") {
        if ($Route::NumLoadedRoutes > 0) { // Just check if routes are loaded
            menu::selectRouteBot(%cl, 0);
        } else {
            Client::sendMessage(%cl, 0, "No routes loaded (this message shouldn't appear if logs are correct).");
            Game::menuRequest(%cl);
        }
        return;
    }
    else if(%selection == "vsmatch") {
         admin::startvote(%cl, "start the match", "smatch", 0);
    } 
    else if(%selection == "vChangeMission") {
         %cl.madeVote = true;
         menu::changemissiontype( %cl, 0 );
         return;
    } else if(%selection == "adminoptions") {
	   //no need to add, falls through to Game::menu request anyway
    }

	Game::menuRequest(%cl); // Default if no other option matched
}
