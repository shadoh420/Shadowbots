// 'Capture The Flag' flag code

$Flag::ReturnTime = 45;

function Flag::objectiveInit( %this ) {
	%this.originalPosition = GameBase::getPosition(%this);
	%this.atHome = true;
	%this.pickupSequence = 0;
	%this.carrier = -1;

	%this.enemyCaps = 0;
	%this.caps[0] = 0;
	%this.caps[1] = 0;
	%this.caps[2] = 0;
	%this.caps[3] = 0;
	%this.caps[4] = 0;
	%this.caps[5] = 0;
	%this.caps[6] = 0;
	%this.caps[7] = 0;

	$Team::Flag[ GameBase::getTeam(%this) ] = %this;

	return ( true );
}


function Flag::getObjectiveString( %this, %forTeam ) {
	%thisTeam = GameBase::getTeam(%this);

	if($missionComplete) {
		if(%thisTeam == %forTeam)
			return "<Bflag_atbase.png>\nYour flag was captured " @ %this.enemyCaps @ " times.";
		else
			return "<Bflag_enemycaptured.png>\nYour team captured the " @ getTeamName(%thisTeam) @ " flag " @ %this.caps[%forTeam] @ " times.";
		return;
	}

	if(%thisTeam == %forTeam) {
		if(%this.atHome)
			return "<Bflag_atbase.png>\nDefend your flag to prevent enemy captures.";
		else if(%this.carrier != -1)
			return "<Bflag_enemycaptured.png>\nReturn your flag to base. (carried by " @ String::escapeFormatting(Client::getName(Player::getClient(%this.carrier))) @ ")";
		else
			return "<Bflag_notatbase.png>\nReturn your flag to base. (dropped in the field)";
	} else {
		if(%this.atHome)
			return "<Bflag_enemycaptured.png>\nGrab the " @ getTeamName(%thisTeam) @ " flag and touch it to your's to score " @ %this.scoreValue @ " points.";
		else if(%this.carrier == -1)
			return "<Bflag_notatbase.png>\nFind the " @ getTeamName(%thisTeam) @ " flag and touch it to your's to score " @ %this.scoreValue @ " points.";
		else if(GameBase::getTeam(%this.carrier) == %forTeam)
			return "<Bflag_atbase.png>\nEscort friendly carrier " @ String::escapeFormatting(Client::getName(Player::getClient(%this.carrier))) @ " to base.";
		else
			return "<Bflag_enemycaptured.png>\nWaylay enemy carrier " @ String::escapeFormatting(Client::getName(Player::getClient(%this.carrier))) @ " and steal his flag.";
	}
}

function Flag::onDrop( %player, %type ) {
	%playerTeam = GameBase::getTeam(%player);
	%flag = %player.carryFlag;
	%flagTeam = GameBase::getTeam(%flag);
	%cl = Player::getClient(%player);
	%name = Client::getName(%cl);
	
	Event::Trigger( eventServerFlagDropped, %cl, %flagTeam );

	message::allExcept( %cl, 0, %name @ " dropped the " @ getTeamName(%flagTeam) @ " flag!" );
	Client::sendMessage( %cl, 0, "You dropped the " @ getTeamName(%flagTeam) @ " flag!" );
	message::teams( 1, %flagTeam, "Your flag was dropped in the field.", -2, "", "The " @ getTeamName(%flagTeam) @ " flag was dropped in the field." );

	GameBase::throw(%flag, %player, 10, false);
	Flag::removeFromPlayer( %flag, %player, false );
	Item::hide(%flag, false);

	%flag.dropFade = 1;
	schedule( "Flag::checkReturn(" @ %flag @ ", " @ %flag.pickupSequence @ ");", $Flag::ReturnTime );
	
	ObjectiveMission::ObjectiveChanged(%flag);
}

function Flag::checkReturn( %flag, %sequenceNum ) {
	if( %flag.pickupSequence != %sequenceNum || %flag.timerOn != "" )
		return;
		
	if( %flag.dropFade ) { 
		GameBase::startFadeOut(%flag);
		%flag.dropFade = "";
		%flag.fadeOut = 1;
		schedule("Flag::checkReturn(" @ %flag @ ", " @ %sequenceNum @ ");", 2.5);
	} else {
		Event::Trigger( eventServerFlagReturned, 0, GameBase::getTeam(%flag) );
		Flag::Return( %flag, true );
	}
}


function Flag::onCollision( %this, %object ) {
	echo("Flag::onCollision: Flag " @ %this @ " (" @ GameBase::getDataName(%this) @ ", team " @ GameBase::getTeam(%this) @ ") collided with Object " @ %object @ " (" @ getObjectType(%object) @ ")");

	if( getObjectType(%object) != "Player" ) {
		echo("Flag::onCollision: Object is not a Player. Exiting.");
		return;
	}

	if( %this.carrier != -1 ) {
		echo("Flag::onCollision: Flag " @ %this @ " is already carried by " @ %this.carrier @ ". Exiting.");
		return;
	}

	// We've already commented this out, but good to remember:
	// if ( Player::isAIControlled(%object) ) {
	//	echo("Flag::onCollision: Object is AI controlled, explicit AI block. Exiting.");
	//	return;
	// }  

	%name = Item::getItemData(%this);
	%playerTeam = GameBase::getTeam(%object);
	%flagTeam = GameBase::getTeam(%this);
	%cl = Player::getClient(%object);
	%touchClientName = Client::getName(%cl);

	echo("Flag::onCollision: Player: " @ %touchClientName @ " (ID: " @ %cl @ ", Team: " @ %playerTeam @ ") touched Flag (Team: " @ %flagTeam @ ", AtHome: " @ %this.atHome @ ")");
	echo("Flag::onCollision: Player's current carryFlag: '" @ %object.carryFlag @ "'");
	echo("Flag::onCollision: Player's vehicle: '" @ %object.vehicle @ "'");
	echo("Flag::onCollision: Player's outArea: '" @ %object.outArea @ "'");


	if( %flagTeam == %playerTeam ) {
		echo("Flag::onCollision: Player touched OWN flag.");
		// ... (your existing logic for own flag return/cap) ...
		// Add echos inside this block too if needed
		if ( !%this.atHome ) {
			echo("Flag::onCollision: Returning own flag.");
			// ...
		} else {
			echo("Flag::onCollision: Attempting to cap with own flag.");
			// ...
		}

	} else { // Player touched an ENEMY flag
		echo("Flag::onCollision: Player touched ENEMY flag.");

		if ( %object.vehicle != "" ) {
			echo("Flag::onCollision: Player is in a vehicle. Cannot pick up. Exiting.");
			return;
		}
		
		if(%object.outArea != "") {
			Client::sendMessage(%cl, 1, "Flag not in mission area."); // This message should appear if this is the issue
			echo("Flag::onCollision: Player is out of mission area. Cannot pick up. Exiting.");
			return;
		}

		echo("Flag::onCollision: Attempting to give flag " @ %name @ " to player " @ %touchClientName);

		// These are the critical lines for pickup:
		Player::setItemCount(%object, Flag, 1);
		echo("Flag::onCollision: After Player::setItemCount(Flag, 1), player " @ %touchClientName @ " item count for Flag: " @ Player::getItemCount(%object, Flag));

		Player::mountItem(%object, Flag, $FlagSlot, %flagTeam); // %flagTeam here is used as the 'skinset' for the flag image
		echo("Flag::onCollision: After Player::mountItem(Flag), mounted item in slot $FlagSlot: " @ Player::getMountedItem(%object, $FlagSlot));
		
		// Check if the above actually worked by seeing if the player is now carrying the flag internally
		if (Player::getItemCount(%object, Flag) > 0 && Player::getMountedItem(%object, $FlagSlot) == "Flag") { // Or Item::getItemData(Player::getMountedItem(...))
			echo("Flag::onCollision: Flag pickup appears successful internally for " @ %touchClientName);
			Item::hide(%this, true);
			%fromField = !%this.atHome;
			if ( %this.atHome )
				%this.initialGrabTime = getSimTime();
			// $flagAtHome[1] = false; // This global seems specific, ensure it's correct for the flagTeam
			$flagAtHome[%flagTeam] = false; // More general
			%this.atHome = false;
			%this.carrier = %object;
			%this.pickupSequence++;
			%object.carryFlag = %this; // CRITICAL: Player object now knows it's carrying this specific flag instance
			Flag::setWaypoint(%cl, %this);
			
			if(%this.fadeOut) {
				GameBase::startFadeIn(%this);
				%this.fadeOut= "";
			}

			Event::Trigger( eventServerFlagTaken, %cl, %flagTeam, %fromField );

			message::allExcept(%cl, 0, %touchClientName @ " took the " @ getTeamName(%flagTeam) @ " flag! ~wflag1.wav");
			Client::sendMessage(%cl, 0, "You took the " @ getTeamName(%flagTeam) @ " flag! ~wflag1.wav");
			message::teams(1, %playerTeam, "Your team has the " @ getTeamName(%flagTeam) @ " flag.", %flagTeam, "Your team's flag has been taken.");

			ObjectiveMission::ObjectiveChanged(%this);
		} else {
			echo("Flag::onCollision: CRITICAL FAILURE - Player::setItemCount or Player::mountItem did NOT result in player carrying flag for " @ %touchClientName);
			// If it fails here, the bot isn't actually "getting" the flag item internally.
		}
	}
}

function Flag::clearWaypoint( %cl, %success ) {
	if ( %success )
		setCommandStatus( %cl, 0, "Objective completed.~wobjcomp");
	else
		setCommandStatus( %cl, 0, "Objective failed.");
}

function Flag::setWaypoint( %cl, %flag ) {
	if(!%cl.autoWaypoint)
		return;

	%flagTeam = GameBase::getTeam(%flag);
	%team = Client::getTeam(%cl);

	%pos = $Team::Flag[%team].originalPosition;
	%posX = getWord(%pos,0);
	%posY = getWord(%pos,1);

	issueCommand( %cl, %cl, 0, "Take the " @ getTeamName(%flagTeam) @ " flag to our flag.~wcapobj", %posX, %posY );
}

function FlagStand::objectiveInit( %this ) {
	return ( false );
}


function FlagStand::onCollision( %this, %object ) {
}

function Flag::clientDropped( %this, %clientId ) {
	%type = Player::getMountedItem( %clientId, $FlagSlot );
	if(%type != -1)
		Player::dropItem(%clientId, %type);
}

function Flag::playerLeaveMissionArea( %this, %player ) {
	if ( %this.carrier != %player )
		return;

	%this.atHome = true;
	%flagTeam = GameBase::getTeam(%this);
	
	%cl = Player::getClient(%player);
	if ( %cl != -1 ) {
		%name = Client::getName( %cl );
		
		message::allExcept(%cl, 0, %name @ " left the mission area while carrying the " @ getTeamName(%team) @ " flag!");
		Client::sendMessage(%cl, 0, "You left the mission area while carrying the " @ getTeamName(%team) @ " flag!");
		message::allExcept(%cl, 0, %name @ " left the mission area while carrying the " @ getTeamName(%team) @ " flag!");
	} else {
		message::allExcept(%cl, 0, "A non-player left the mission area while carrying the " @ getTeamName(%team) @ " flag!");
	}

	message::teams(1, %team, "Your flag was returned to base.~wflagreturn.wav", -2, "", "The " @ getTeamName(%team) @ " flag was returned to base.");

	Event::Trigger( eventServerFlagReturned, 0, %flagTeam );

	Flag::removeFromPlayer( %this, %player, false );
	Flag::Return( %this, true );	

	ObjectiveMission::checkScoreLimit();
}


function Flag::removeFromPlayer( %this, %player, %success ) {
	%this.carrier = -1;
	
	%player.carryFlag = "";
	Flag::clearWaypoint( Player::getClient( %player ), %success );
	Player::setItemCount( %player, "Flag", 0 );
}

function Flag::Return( %this, %announce ) {
	%team = GameBase::getTeam( %this );

	GameBase::setPosition( %this, %this.originalPosition );
	Item::hide( %this, false );
	Item::setVelocity( %this, "0 0 0" );
	GameBase::startFadeIn( %this );
	
	%this.atHome = true;
	%this.fadeOut = "";

	if ( %announce != "" )
		message::teams( 0, %team, "Your flag was returned to base.~wflagreturn.wav", -2, "", "The " @ getTeamName(%team) @ " flag was returned to base.~wflagreturn.wav" );
}





// REMOTES

function remoteFlagstandWaypoint( %cl, %team ) {
	if ( !$Team::Flag[%team] )
		return;

	%pos = $Team::Flag[%team].originalPosition;
	%posX = getWord(%pos,0);
	%posY = getWord(%pos,1);
	%side = ( Client::getTeam(%cl) == %team ) ? "FRIENDLY" : "ENEMY";

	issueCommand( %cl, %cl, 0, "Waypoint set to the " @ %side @ " flagstand", %posX, %posY );
}
